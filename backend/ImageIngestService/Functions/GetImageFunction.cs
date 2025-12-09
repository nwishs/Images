using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using ImageIngestLambda.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageIngestLambda.Functions
{
    public class GetImageFunction
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AwsSettings _settings;
        private readonly TimeSpan _signedUrlTtl = TimeSpan.FromMinutes(15);

        public GetImageFunction() : this(new AmazonS3Client(), new AwsSettings())
        {
        }

        public GetImageFunction(IAmazonS3 s3Client, AwsSettings settings)
        {
            _s3Client = s3Client;
            _settings = settings;
        }

        public APIGatewayProxyResponse Handle(APIGatewayProxyRequest request, ILambdaContext? context)
        {
            var itemId = ResolveItemId(request);
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return BuildResponse(HttpStatusCode.BadRequest, new { Error = "ItemId is required in the path." });
            }

            try
            {
                var urls = GetPresignedUrlsAsync(itemId).GetAwaiter().GetResult();
                return BuildResponse(HttpStatusCode.OK, new { ItemId = itemId, Urls = urls });
            }
            catch (Exception ex)
            {
                context?.Logger?.LogLine($"Failed to generate presigned URLs for ItemId {itemId}: {ex.Message}");
                return BuildResponse(HttpStatusCode.InternalServerError, new { Error = "Failed to load images." });
            }
        }

        private async Task<IReadOnlyList<string>> GetPresignedUrlsAsync(string itemId)
        {
            var prefix = $"{itemId.TrimEnd('/')}/";

            var request = new ListObjectsV2Request
            {
                BucketName = _settings.RepositoryBucket,
                Prefix = prefix
            };

            var urls = new List<string>();
            ListObjectsV2Response response;
            do
            {
                response = await _s3Client.ListObjectsV2Async(request);

                foreach (var obj in response.S3Objects.Where(o => !o.Key.EndsWith("/")))
                {
                    var urlRequest = new GetPreSignedUrlRequest
                    {
                        BucketName = _settings.RepositoryBucket,
                        Key = obj.Key,
                        Expires = DateTime.UtcNow.Add(_signedUrlTtl),
                        Verb = HttpVerb.GET
                    };

                    urls.Add(_s3Client.GetPreSignedURL(urlRequest));
                }

                request.ContinuationToken = response.IsTruncated ? response.NextContinuationToken : null;

            } while (response.IsTruncated);

            return urls;
        }

        private static string? ResolveItemId(APIGatewayProxyRequest request)
        {
            if (request.PathParameters != null)
            {
                if (request.PathParameters.TryGetValue("itemId", out var pathItemId) && !string.IsNullOrWhiteSpace(pathItemId))
                {
                    return pathItemId.Trim();
                }

                if (request.PathParameters.TryGetValue("proxy", out var proxyId) && !string.IsNullOrWhiteSpace(proxyId))
                {
                    return proxyId.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                }
            }

            var path = request.Path ?? string.Empty;
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Path is typically /{stage}/{itemId} or /{itemId}
            if (parts.Length >= 1)
            {
                return parts.Last();
            }

            return null;
        }

        private static APIGatewayProxyResponse BuildResponse(HttpStatusCode statusCode, object payload)
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = (int)statusCode,
                Body = JsonSerializer.Serialize(payload),
                Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } }
            };
        }
    }
}
