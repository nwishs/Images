using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using ImageIngestLambda.Config;
using ImageIngestLambda.Models;
using ImageIngestLambda.Services;

namespace ImageIngestLambda.Functions
{
    public class PostImageFunction
    {
        private const string ImagesTableName = "Images";

        private readonly IAmazonS3 _s3Client;
        private readonly IAmazonDynamoDB _dynamoDb;
        private readonly HttpClient _httpClient;
        private readonly AwsSettings _settings;
        private readonly SqsPublisher _sqsPublisher;
        private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

        public PostImageFunction()
            : this(new AmazonS3Client(), new AmazonDynamoDBClient(), new AmazonSQSClient(), new HttpClient(), new AwsSettings())
        {
        }

        public PostImageFunction(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb, IAmazonSQS sqsClient, HttpClient httpClient, AwsSettings settings)
        {
            _s3Client = s3Client;
            _dynamoDb = dynamoDb;
            _sqsPublisher = new SqsPublisher(sqsClient, settings);
            _httpClient = httpClient;
            _settings = settings;
        }

        public APIGatewayProxyResponse Handle(APIGatewayProxyRequest request, ILambdaContext? context)
        {
            return HandleAsync(request, context).GetAwaiter().GetResult();
        }

        public async Task<APIGatewayProxyResponse> HandleAsync(APIGatewayProxyRequest request, ILambdaContext? context)
        {
            if (request == null)
            {
                return BuildErrorResponse(HttpStatusCode.BadRequest, "Request payload is required.");
            }

            var payload = request.IsBase64Encoded
                ? DecodeBase64(request.Body)
                : request.Body;

            ImagePostRequest? model;
            try
            {
                model = JsonSerializer.Deserialize<ImagePostRequest>(payload ?? string.Empty, _serializerOptions);
            }
            catch (JsonException ex)
            {
                context?.Logger?.LogLine($"Failed to deserialize POST body. Error: {ex.Message}");
                return BuildErrorResponse(HttpStatusCode.BadRequest, "Invalid request payload.");
            }

            if (model == null || string.IsNullOrWhiteSpace(model.ItemId) || model.PhotoUrls == null || !model.PhotoUrls.Any())
            {
                return BuildErrorResponse(HttpStatusCode.BadRequest, "itemId and photoUrls are required.");
            }

            try
            {
                await EnsureFolderExistsAsync(model.ItemId);
            }
            catch (Exception ex)
            {
                context?.Logger?.LogLine($"Failed to create folder for ItemId {model.ItemId}: {ex.Message}");
                return BuildErrorResponse(HttpStatusCode.InternalServerError, "Failed to prepare S3 folder.");
            }

            var processed = new List<object>();

            foreach (var photoUrl in model.PhotoUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
            {
                var cleanedUrl = photoUrl.Trim();
                context?.Logger?.LogLine($"ItemId: {model.ItemId}, Url: {cleanedUrl}");

                try
                {
                    var (imageId, objectKey, fileName) = BuildObjectKey(model.ItemId, cleanedUrl);

                    if (await IsImageRegisteredAsync(model.ItemId, imageId))
                    {
                        context?.Logger?.LogLine($"Image already registered for ItemId {model.ItemId} and ImageId {imageId}. Skipping.");
                        continue;
                    }

                    var download = await DownloadImageAsync(cleanedUrl);
                    await using var contentStream = download.Content;

                    await UploadToS3Async(objectKey, contentStream, download.ContentType);

                    var s3Url = BuildS3Url(objectKey);

                    await WriteDynamoRecordAsync(model.ItemId, imageId, s3Url);
                    await SendSqsEventsAsync(model.ItemId, s3Url, context);

                    processed.Add(new { imageId, fileName, s3Url });
                }
                catch (Exception ex)
                {
                    context?.Logger?.LogLine($"Failed processing {photoUrl}: {ex.Message}");
                    return BuildErrorResponse(HttpStatusCode.InternalServerError, $"Failed processing image: {photoUrl}");
                }
            }

            return new APIGatewayProxyResponse
            {
                StatusCode = (int)HttpStatusCode.Created,
                Body = JsonSerializer.Serialize(new { Message = "Images ingested", Items = processed }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }

        private static string DecodeBase64(string? encoded)
        {
            if (string.IsNullOrEmpty(encoded))
            {
                return string.Empty;
            }

            var bytes = Convert.FromBase64String(encoded);
            return Encoding.UTF8.GetString(bytes);
        }

        private static (string imageId, string objectKey, string fileName) BuildObjectKey(string itemId, string photoUrl)
        {
            var uri = new Uri(photoUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"{Guid.NewGuid():N}";
            }

            var imageId = Path.GetFileNameWithoutExtension(fileName);
            var objectKey = $"{itemId.TrimEnd('/')}/{fileName}";

            return (imageId, objectKey, fileName);
        }

        private async Task<(MemoryStream Content, string ContentType)> DownloadImageAsync(string url)
        {
            using var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Download failed with status {(int)response.StatusCode}");
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            await using var responseStream = await response.Content.ReadAsStreamAsync();

            var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return (memoryStream, contentType);
        }

        private async Task UploadToS3Async(string objectKey, Stream contentStream, string contentType)
        {
            var request = new PutObjectRequest
            {
                BucketName = _settings.RepositoryBucket,
                Key = objectKey,
                InputStream = contentStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request);
        }

        private string BuildS3Url(string objectKey)
        {
            var encodedKey = Uri.EscapeDataString(objectKey).Replace("%2F", "/");
            return $"https://{_settings.RepositoryBucket}.s3.amazonaws.com/{encodedKey}";
        }

        private Task EnsureFolderExistsAsync(string itemId)
        {
            var folderKey = $"{itemId.TrimEnd('/')}/";

            var request = new PutObjectRequest
            {
                BucketName = _settings.RepositoryBucket,
                Key = folderKey,
                ContentBody = string.Empty
            };

            return _s3Client.PutObjectAsync(request);
        }

        private Task WriteDynamoRecordAsync(string itemId, string imageId, string s3Url)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["ItemId"] = new AttributeValue { S = itemId },
                ["ImageId"] = new AttributeValue { S = imageId },
                ["format"] = new AttributeValue { S = "ORIGINAL" },
                ["url"] = new AttributeValue { S = s3Url }
            };

            var request = new PutItemRequest
            {
                TableName = ImagesTableName,
                Item = item
            };

            return _dynamoDb.PutItemAsync(request);
        }

        private async Task<bool> IsImageRegisteredAsync(string itemId, string imageId)
        {
            var request = new GetItemRequest
            {
                TableName = ImagesTableName,
                Key = new Dictionary<string, AttributeValue> { ["ImageId"] = new AttributeValue { S = imageId } },
                ProjectionExpression = "ItemId, ImageId, #fmt",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#fmt"] = "format" }
            };

            var response = await _dynamoDb.GetItemAsync(request);

            if (response.Item == null || response.Item.Count == 0)
            {
                return false;
            }

            response.Item.TryGetValue("ItemId", out var storedItemId);
            if (!string.Equals(storedItemId?.S, itemId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (response.Item.TryGetValue("format", out var formatAttr))
            {
                return string.Equals(formatAttr.S, "ORIGINAL", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private Task SendSqsEventsAsync(string itemId, string s3Url, ILambdaContext? context)
        {
            var formats = new[] { "32px", "100px", "200px", "blurred" };
            var tasks = new List<Task>(formats.Length);

            foreach (var format in formats)
            {
                context?.Logger?.LogLine($"Queueing SQS event for ItemId: {itemId}, Url: {s3Url}, Format: {format}");
                tasks.Add(_sqsPublisher.PublishAsync(itemId, s3Url, format));
            }

            return Task.WhenAll(tasks);
        }

        private static APIGatewayProxyResponse BuildErrorResponse(HttpStatusCode statusCode, string message)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonSerializer.Serialize(new { Error = message }),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }
}
