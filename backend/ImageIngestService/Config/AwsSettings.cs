using System.Text.Json.Serialization;

namespace ImageIngestLambda.Config
{
    public class AwsSettings
    {
        [JsonPropertyName("repository_bucket")]
        public string RepositoryBucket { get; set; } = "carimagesrepository2";

        [JsonPropertyName("sqs_queue_url")]
        public string SqsQueueUrl { get; set; } = "https://sqs.ap-southeast-2.amazonaws.com/649988449397/car-images-events";
    }
}
