namespace Orleans.EventSourcing.EventStorage.Models
{
    using Newtonsoft.Json;

    public class Snapshot<TLogView>
    {
        public string Id { get { return $"[{Source?.Type}][{Source?.Id}]"; } }

        [JsonProperty("partitionId")]
        public string PartitionId { get { return "snapshot"; } }

        public CommitSource Source { get; set; }

        public TLogView State { get; set; }
    }
}
