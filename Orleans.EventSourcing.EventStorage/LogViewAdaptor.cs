namespace Orleans.EventSourcing.EventStorage
{
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Azure.Documents.Linq;
    using Newtonsoft.Json;
    using Orleans.EventSourcing.Common;
    using Orleans.EventSourcing.EventStorage.Models;
    using Orleans.EventSourcing.EventStorage.StoredProcedures;
    using Orleans.LogConsistency;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class LogViewAdaptor<TLogView, TLogEntry> : PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, SubmissionEntry<TLogEntry>> where TLogView : class, new() where TLogEntry : class
    {
        private string _grainTypeName;
        private DocumentClient _client;
        private DocumentClient _storedProcClient;
        private JsonSerializerSettings _serializerSettings;
        private EventStorageOptions _options;
        private Uri _databaseUri;
        private Uri _collectionUri;
        private Uri _commitStoredProcUri;

        // the confirmed view
        private IList<Commit> _commits;
        private TLogView _confirmedViewInternal;
        private int _confirmedVersionInternal;

        static Dictionary<string, Commit> _commitDb = new Dictionary<string, Commit>();

        public LogViewAdaptor(ILogViewAdaptorHost<TLogView, TLogEntry> host, TLogView initialState, string grainTypeName, ILogConsistencyProtocolServices services, DocumentClient dbClient, DocumentClient spClient, EventStorageOptions options, JsonSerializerSettings serializerSettings ) : base(host, initialState, services)
        {
            _grainTypeName = grainTypeName;
            _client = dbClient;
            _storedProcClient = spClient;
            _serializerSettings = serializerSettings;
            _options = options;

            _databaseUri = UriFactory.CreateDatabaseUri(options.DatabaseName);
            _collectionUri = UriFactory.CreateDocumentCollectionUri(options.DatabaseName, options.CollectionName);
            _commitStoredProcUri = UriFactory.CreateStoredProcedureUri(options.DatabaseName, options.CollectionName, ServerSideRegistry.StoredProcedures.Commit);
        }

        public override Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int toVersion)
        {
            throw new NotImplementedException();
        }

        protected override int GetConfirmedVersion()
        {
            return _confirmedVersionInternal;
        }

        protected override TLogView LastConfirmedView()
        {
            return _confirmedViewInternal;
        }

        protected override void InitializeConfirmedView(TLogView initialstate)
        {
            _confirmedViewInternal = initialstate;
            _confirmedVersionInternal = 0;
            _commits = new List<Commit>();
        }

        protected override SubmissionEntry<TLogEntry> MakeSubmissionEntry(TLogEntry entry)
        {
            return new SubmissionEntry<TLogEntry>() { Entry = entry };
        }

        protected override async Task ReadAsync()
        {
            var startCommit = 0;
            var grainId = Services.GrainReference.ToKeyString();

            // Check for a snapshot
            try
            {
                var snapshotUri = UriFactory.CreateDocumentUri(_options.DatabaseName, _options.CollectionName, $"[{_grainTypeName}][{grainId}]");
                var snapshot = await _client.ReadDocumentAsync<Snapshot<TLogView>>(snapshotUri, new RequestOptions { PartitionKey = new PartitionKey("snapshot") });

                if (snapshot != null && snapshot.Document != null) {
                    _confirmedViewInternal = snapshot.Document.State;
                    _confirmedVersionInternal = snapshot.Document.Source.Version;
                }
            }
            catch(DocumentClientException dce)
            {

            }
            catch(Exception e)
            {

            }

            // Load additional commits for this type from that snapshot
           var options = new FeedOptions
           {
               MaxItemCount = 100,
               PartitionKey = new PartitionKey("commit")
           };

            var query = _client.CreateDocumentQuery<Commit>(_collectionUri, options)
                .Where(c => c.Number > startCommit && c.Source.Id == grainId && c.Source.Type == _grainTypeName).OrderBy(c => c.Number).AsDocumentQuery();

            while (query.HasMoreResults)
            {
                foreach (var commit in await query.ExecuteNextAsync<Commit>())
                {
                    _commits.Add(commit);

                    foreach (var @event in commit.Events)
                    {
                        Host.UpdateView(_confirmedViewInternal, (TLogEntry)@event);
                    }

                    _confirmedVersionInternal = commit.Number;
                }
            }
        }

        protected override async Task<int> WriteAsync()
        {
            var updates = GetCurrentBatchOfUpdates();
            var grainId = Services.GrainReference.ToKeyString();

            var nextVersion = _confirmedVersionInternal + 1;

            var commitSource = new CommitSource
            {
                Id = grainId,
                Type = _grainTypeName,
                Version = nextVersion
            };

            var events = new List<object>();

            foreach (var update in updates)
            {
                events.Add(update.Entry);
            }


            var commitRequest = new CommitRequest(commitSource, events);
            var commitRequestString = JsonConvert.SerializeObject(commitRequest, _serializerSettings);


            var opts = new RequestOptions()
            {
                PartitionKey = new PartitionKey(commitRequest.PartitionId)
            };

            var commitResult = await _storedProcClient.ExecuteStoredProcedureAsync<string>(_commitStoredProcUri, opts, commitRequestString);
            var commit = JsonConvert.DeserializeObject<Commit>(commitResult.Response);

            //var commit = new Commit(commitNumber, nextVersion, commitSource, events);
            //await _client.UpsertDocumentAsync(_collectionUri, commit);

            _commits.Add(commit);

            foreach (var @event in events)
            {
                Host.UpdateView(_confirmedViewInternal, (TLogEntry)@event);
            }

            if(_options.TakeSnapshots &&  commit.Source.Version % _options.CommitsPerSnapshot == 0)
            {
                var snapshot = new Snapshot<TLogView>
                {
                    Source = commitSource,
                    State = _confirmedViewInternal
                };

                await _client.UpsertDocumentAsync(_collectionUri, snapshot, new RequestOptions { PartitionKey = new PartitionKey(snapshot.PartitionId) });
            }

            if(_options.PostCommit != null)
            {
                await _options.PostCommit(commit);
            }

            _confirmedVersionInternal = commit.Source.Version;

            return updates.Length;
        }
    }
}
