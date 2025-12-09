using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents; // This is the crucial namespace for SQS triggers
using Amazon.DynamoDBv2;
using Amazon.S3;
using SqsProcessor.Services;
using System.Threading.Tasks;
using System;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace SqsProcessor
{
    public class Function
    {
        private readonly IAmazonS3 _s3Client = new AmazonS3Client();
        private readonly IAmazonDynamoDB _dynamoDb = new AmazonDynamoDBClient();

        /// <summary>
        /// This method is the entry point for the Lambda function.
        /// It receives a batch of messages in the SQSEvent object.
        /// </summary>
        /// <param name="sqsEvent">The event data containing SQS messages.</param>
        /// <param name="context">The context for the Lambda function.</param>
        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            // Log the number of records (messages) received in the batch
            context.Logger.LogLine($"Received {sqsEvent.Records.Count} records from SQS.");

            foreach (var message in sqsEvent.Records)
            {
                await ProcessMessageAsync(message, context);
            }
        }

        private async Task ProcessMessageAsync(SQSEvent.SQSMessage message, ILambdaContext context)
        {
            context.Logger.LogLine($"[Message ID: {message.MessageId}]");
            context.Logger.LogLine($"[Source Queue ARN: {message.EventSourceArn}]");

            if (!message.MessageAttributes.TryGetValue("ItemId", out var itemIdAttr) ||
                string.IsNullOrWhiteSpace(itemIdAttr.StringValue))
            {
                context.Logger.LogLine("Missing ItemId attribute; skipping message.");
                return;
            }

            if (!message.MessageAttributes.TryGetValue("S3URL", out var s3UrlAttr) ||
                string.IsNullOrWhiteSpace(s3UrlAttr.StringValue))
            {
                context.Logger.LogLine("Missing S3URL attribute; skipping message.");
                return;
            }

            if (!message.MessageAttributes.TryGetValue("format", out var formatAttr) ||
                string.IsNullOrWhiteSpace(formatAttr.StringValue))
            {
                context.Logger.LogLine("Missing format attribute; skipping message.");
                return;
            }

            var formatValue = formatAttr.StringValue!.Trim();
            var processor = ResolveProcessor(formatValue);
            if (processor == null)
            {
                context.Logger.LogLine($"Unsupported format '{formatValue}'; skipping message.");
                return;
            }

            try
            {
                var outputUrl = await processor.ProcessImageAsync(itemIdAttr.StringValue!, s3UrlAttr.StringValue!);
                context.Logger.LogLine($"Processed image for ItemId {itemIdAttr.StringValue} with format {processor.Format}. Output: {outputUrl}");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error processing message {message.MessageId}: {ex.Message}");
                throw;
            }
        }

        private IImageProcessor? ResolveProcessor(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "32px":
                    return new ImageProcessor32Px(_s3Client, _dynamoDb);
                case "100px":
                    return new ImageProcessor100Px(_s3Client, _dynamoDb);
                case "200px":
                    return new ImageProcessor200Px(_s3Client, _dynamoDb);
                case "blurred":
                    return new ImageProcessorBlurred(_s3Client, _dynamoDb);
                default:
                    return null;
            }
        }
    }
}
