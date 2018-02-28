namespace Orleans.EventSourcing.EventStorage.Models
{
    using Newtonsoft.Json;

    public class Commit : CommitRequest
    {
        [JsonProperty("id")]
        public string Id { get { return Number.ToString(); } }

        [JsonProperty("number")]
        public int Number { get; private set; }
    }
}
