using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using ImageIngestLambda.Config;

namespace ImageIngestLambda.Services
{
    public class SqsPublisher
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly AwsSettings _settings;

        public SqsPublisher(IAmazonSQS sqsClient, AwsSettings settings)
        {
            _sqsClient = sqsClient;
            _settings = settings;
        }

        public Task PublishAsync(string itemId, string s3Url, string format)
        {
            var body = JsonSerializer.Serialize(new { itemId, s3Url, format });

            var request = new SendMessageRequest
            {
                QueueUrl = _settings.SqsQueueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["ItemId"] = new MessageAttributeValue { DataType = "String", StringValue = itemId },
                    ["S3URL"] = new MessageAttributeValue { DataType = "String", StringValue = s3Url },
                    ["format"] = new MessageAttributeValue { DataType = "String", StringValue = format }
                }
            };

            return _sqsClient.SendMessageAsync(request);
        }
    }
}
