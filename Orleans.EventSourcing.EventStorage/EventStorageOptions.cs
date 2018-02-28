namespace Orleans.EventSourcing.EventStorage
{
    using Microsoft.Azure.Documents.Client;
    using Orleans.EventSourcing.EventStorage.Models;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading.Tasks;

    public class EventStorageOptions
    {
        public string AccountEndpoint { get; set; } = "https://localhost:8081";

        public string AccountKey { get; set; } = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        public ConnectionMode ConnectionMode { get; set; }

        public Protocol ConnectionProtocol { get; set; }

        public string DatabaseName { get; set; } = "Orleans";

        public string CollectionName { get; set; } = "Events";

        public string PartitionKey { get; set; } = "partitionId";

        public int Throughput { get; set; } = 400;

        public bool CanCreateResources { get; set; } = true;

        public bool DropDatabaseOnInit { get; set; } = false;

        public bool AutoUpdateStoredProcedures { get; set; } = true;

        public bool TakeSnapshots { get; set; } = true;

        public int CommitsPerSnapshot { get; set; } = 100;

        public Func<Commit, Task> PostCommit {get; set; }
    }
}
