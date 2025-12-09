using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ImageIngestLambda.Models
{
    public class ImagePostRequest
    {
        [JsonPropertyName("itemId")]
        public string ItemId { get; set; } = string.Empty;

        [JsonPropertyName("photoUrls")]
        public List<string> PhotoUrls { get; set; } = new();
    }
}
