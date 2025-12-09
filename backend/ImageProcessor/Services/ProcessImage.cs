using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace SqsProcessor.Services
{
    public interface IImageProcessor
    {
        string Format { get; }
        Task<string> ProcessImageAsync(string itemId, string sourceUrl);
    }

    public abstract class BaseImageProcessor : IImageProcessor
    {
        protected const string TargetBucket = "carimagesrepository2";
        protected const string DefaultImagesTable = "Images";
        protected readonly IAmazonS3 S3Client;
        protected readonly IAmazonDynamoDB DynamoDb;
        private readonly string _imagesTableName;

        public abstract string Format { get; }

        protected BaseImageProcessor(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb)
        {
            S3Client = s3Client;
            DynamoDb = dynamoDb;
            _imagesTableName = Environment.GetEnvironmentVariable("IMAGES_TABLE") ?? DefaultImagesTable;
            if (string.IsNullOrWhiteSpace(_imagesTableName))
            {
                _imagesTableName = DefaultImagesTable;
            }
        }

        public virtual async Task<string> ProcessImageAsync(string itemId, string sourceUrl)
        {
            var (sourceBucket, sourceKey) = ParseS3Url(sourceUrl);
            var (imageId, extension) = ExtractImageInfo(sourceKey);

            var destinationKey = $"{itemId}/{imageId}_{Format}{extension}";

            var request = new CopyObjectRequest
            {
                SourceBucket = sourceBucket,
                SourceKey = sourceKey,
                DestinationBucket = TargetBucket,
                DestinationKey = destinationKey
            };

            await S3Client.CopyObjectAsync(request);

            var encodedKey = Uri.EscapeDataString(destinationKey).Replace("%2F", "/");
            var outputUrl = $"https://{TargetBucket}.s3.amazonaws.com/{encodedKey}";

            await WriteDynamoRecordAsync(itemId, imageId, outputUrl);
            return outputUrl;
        }

        protected static (string bucket, string key) ParseS3Url(string url)
        {
            var uri = new Uri(url);
            var hostParts = uri.Host.Split('.');
            var bucket = hostParts[0];
            var key = uri.AbsolutePath.TrimStart('/');

            if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Invalid S3 URL provided.");
            }

            return (bucket, key);
        }

        protected static (string imageId, string extension) ExtractImageInfo(string key)
        {
            var fileName = Path.GetFileName(key);
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".jpg";
            }

            var imageId = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(imageId))
            {
                imageId = Guid.NewGuid().ToString("N");
            }

            return (imageId, extension);
        }

        protected Task WriteDynamoRecordAsync(string itemId, string imageId, string url)
        {
            var formattedImageId = $"{imageId}_{Format.ToUpperInvariant()}";
            var item = new Dictionary<string, AttributeValue>
            {
                ["ItemId"] = new AttributeValue { S = itemId },
                ["ImageId"] = new AttributeValue { S = formattedImageId },
                ["format"] = new AttributeValue { S = Format },
                ["url"] = new AttributeValue { S = url }
            };

            var request = new PutItemRequest
            {
                TableName = _imagesTableName,
                Item = item
            };

            return DynamoDb.PutItemAsync(request);
        }
    }

    public abstract class ResizingImageProcessor : BaseImageProcessor
    {
        protected abstract int TargetSize { get; }

        protected ResizingImageProcessor(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb) : base(s3Client, dynamoDb)
        {
        }

        public override async Task<string> ProcessImageAsync(string itemId, string sourceUrl)
        {
            var (sourceBucket, sourceKey) = ParseS3Url(sourceUrl);
            var (imageId, extension) = ExtractImageInfo(sourceKey);
            var destinationKey = $"{itemId}/{imageId}_{Format}{extension}";

            using var sourceResponse = await S3Client.GetObjectAsync(sourceBucket, sourceKey);
            await using var sourceStream = new MemoryStream();
            await sourceResponse.ResponseStream.CopyToAsync(sourceStream);
            sourceStream.Position = 0;

            var detectedFormat = await Image.DetectFormatAsync(sourceStream);
            sourceStream.Position = 0;

            using var image = await Image.LoadAsync(sourceStream);
            var resizeOptions = new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(TargetSize, TargetSize)
            };

            image.Mutate(ctx => ctx.Resize(resizeOptions));

            var (encoder, contentType) = ResolveEncoder(detectedFormat, image);

            await using var outputStream = new MemoryStream();
            await image.SaveAsync(outputStream, encoder);
            outputStream.Position = 0;

            var putRequest = new PutObjectRequest
            {
                BucketName = TargetBucket,
                Key = destinationKey,
                InputStream = outputStream,
                ContentType = contentType
            };

            await S3Client.PutObjectAsync(putRequest);

            var encodedKey = Uri.EscapeDataString(destinationKey).Replace("%2F", "/");
            var outputUrl = $"https://{TargetBucket}.s3.amazonaws.com/{encodedKey}";

            await WriteDynamoRecordAsync(itemId, imageId, outputUrl);
            return outputUrl;
        }

        private static (IImageEncoder Encoder, string ContentType) ResolveEncoder(IImageFormat? detectedFormat, Image image)
        {
            if (detectedFormat != null)
            {
                try
                {
                    var encoder = image.Configuration.ImageFormatsManager.GetEncoder(detectedFormat);
                    return (encoder, detectedFormat.DefaultMimeType);
                }
                catch (UnknownImageFormatException)
                {
                    // Fall back to JPEG when an encoder is not available.
                }
            }

            return (new JpegEncoder(), "image/jpeg");
        }
    }

    public class ImageProcessor32Px : ResizingImageProcessor
    {
        public override string Format => "32px";
        protected override int TargetSize => 32;

        public ImageProcessor32Px(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb) : base(s3Client, dynamoDb)
        {
        }
    }

    public class ImageProcessor100Px : ResizingImageProcessor
    {
        public override string Format => "100px";
        protected override int TargetSize => 100;

        public ImageProcessor100Px(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb) : base(s3Client, dynamoDb)
        {
        }
    }

    public class ImageProcessor200Px : ResizingImageProcessor
    {
        public override string Format => "200px";
        protected override int TargetSize => 200;

        public ImageProcessor200Px(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb) : base(s3Client, dynamoDb)
        {
        }
    }

    public class ImageProcessorBlurred : BaseImageProcessor
    {
        public override string Format => "blurred";

        public ImageProcessorBlurred(IAmazonS3 s3Client, IAmazonDynamoDB dynamoDb) : base(s3Client, dynamoDb)
        {
        }

        public override async Task<string> ProcessImageAsync(string itemId, string sourceUrl)
        {
            var (sourceBucket, sourceKey) = ParseS3Url(sourceUrl);
            var (imageId, extension) = ExtractImageInfo(sourceKey);
            var destinationKey = $"{itemId}/{imageId}_{Format}{extension}";

            using var sourceResponse = await S3Client.GetObjectAsync(sourceBucket, sourceKey);
            await using var sourceStream = new MemoryStream();
            await sourceResponse.ResponseStream.CopyToAsync(sourceStream);
            sourceStream.Position = 0;

            var detectedFormat = await Image.DetectFormatAsync(sourceStream);
            sourceStream.Position = 0;

            using var image = await Image.LoadAsync(sourceStream);
            image.Mutate(ctx => ctx.GaussianBlur());

            IImageEncoder encoder;
            string contentType;

            if (detectedFormat != null)
            {
                try
                {
                    encoder = image.Configuration.ImageFormatsManager.GetEncoder(detectedFormat);
                    contentType = detectedFormat.DefaultMimeType;
                }
                catch (UnknownImageFormatException)
                {
                    encoder = new JpegEncoder();
                    contentType = "image/jpeg";
                }
            }
            else
            {
                encoder = new JpegEncoder();
                contentType = "image/jpeg";
            }

            await using var outputStream = new MemoryStream();
            await image.SaveAsync(outputStream, encoder);
            outputStream.Position = 0;

            var putRequest = new PutObjectRequest
            {
                BucketName = TargetBucket,
                Key = destinationKey,
                InputStream = outputStream,
                ContentType = contentType
            };

            await S3Client.PutObjectAsync(putRequest);

            var encodedKey = Uri.EscapeDataString(destinationKey).Replace("%2F", "/");
            var outputUrl = $"https://{TargetBucket}.s3.amazonaws.com/{encodedKey}";

            await WriteDynamoRecordAsync(itemId, imageId, outputUrl);
            return outputUrl;
        }
    }
}
