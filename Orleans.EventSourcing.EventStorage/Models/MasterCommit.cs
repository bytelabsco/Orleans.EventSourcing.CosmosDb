namespace Orleans.EventSourcing.EventStorage.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class MasterCommit
    {
        [JsonProperty("id")]
        public string Id { get { return Number.ToString(); } }

        [JsonProperty("partitionId")]
        public string PartitionId { get { return "commit"; } }

        [JsonProperty("number")]
        public int Number { get; set; }
    }
}
