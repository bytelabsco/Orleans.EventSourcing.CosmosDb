namespace Orleans.EventSourcing.EventStorage.Models
{
    using Newtonsoft.Json;

    public class CommitSource
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("version")]
        public int Version { get; set; }
    }
}
