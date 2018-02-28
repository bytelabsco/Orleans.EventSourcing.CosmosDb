namespace Orleans.EventSourcing.EventStorage.Models
{
    using Newtonsoft.Json;
    using System.Collections.Generic;

    public class CommitRequest
    {
        internal CommitRequest()
        {
        }

        public CommitRequest(CommitSource commitSource, IList<object> events)
        {
            Source = commitSource;
            Events = events;
        }

        [JsonProperty("partitionId")]
        public string PartitionId { get { return "commit"; } }

        [JsonProperty("source")]
        public CommitSource Source { get; set; }

        [JsonProperty("events")]
        public IList<object> Events { get; set; }
    }
}
