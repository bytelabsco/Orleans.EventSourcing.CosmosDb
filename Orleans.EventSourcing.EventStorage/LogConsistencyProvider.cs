namespace Orleans.EventSourcing.EventStorage
{
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Orleans.EventSourcing.EventStorage.Models;
    using Orleans.EventSourcing.EventStorage.StoredProcedures;
    using Orleans.LogConsistency;
    using Orleans.Providers;
    using Orleans.Runtime;
    using Orleans.Storage;
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Threading.Tasks;

    public class LogConsistencyProvider : ILogConsistencyProvider
    {
        private ILogger _logger;
        private JsonSerializerSettings _serializerSettings;
        private DocumentClient _dbClient;
        private DocumentClient _spClient;

        public Logger Log { get; private set; }

        public string Name { get; private set; }

        private Guid _serviceId;
        private EventStorageOptions _options;
        private ILoggerFactory _loggerFactory;

        public bool UsesStorageProvider
        {
            get { return false; }
        }

        public async Task Init(string name, IProviderRuntime providerRuntime, IProviderConfiguration config)
        {
            Name = name;
            _serviceId = providerRuntime.ServiceId;
            _options = providerRuntime.ServiceProvider.GetRequiredService<IOptions<EventStorageOptions>>().Value;
            _loggerFactory = providerRuntime.ServiceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _loggerFactory.CreateLogger(nameof(LogConsistencyProvider)); 

            // TODO - use these serializer settings in the client
            _serializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                TypeNameHandling = TypeNameHandling.All
            };

            _dbClient = new DocumentClient(new Uri(_options.AccountEndpoint), _options.AccountKey, _serializerSettings,
                new ConnectionPolicy
                {
                    ConnectionMode = _options.ConnectionMode,
                    ConnectionProtocol = _options.ConnectionProtocol
                });

            _spClient = new DocumentClient(new Uri(_options.AccountEndpoint), _options.AccountKey,
                new ConnectionPolicy
                {
                    ConnectionMode = _options.ConnectionMode,
                    ConnectionProtocol = _options.ConnectionProtocol
                });


            await _dbClient.OpenAsync();

            if (_options.CanCreateResources)
            {
                if (_options.DropDatabaseOnInit)
                {
                    await TryDeleteDatabase();
                }

                await TryCreateDatabaseResources();

                if (_options.AutoUpdateStoredProcedures)
                {
                    await UpdateServerSideLogic();
                }
            }
        }

        public Task Close()
        {
            _dbClient.Dispose();
            return Task.CompletedTask;
        }

        public ILogViewAdaptor<TLogView, TLogEntry> MakeLogViewAdaptor<TLogView, TLogEntry>(ILogViewAdaptorHost<TLogView, TLogEntry> hostGrain, TLogView initialState, string grainTypeName, IStorageProvider storageProvider, ILogConsistencyProtocolServices services)
            where TLogView : class, new()
            where TLogEntry : class
        {
            return new LogViewAdaptor<TLogView, TLogEntry>(hostGrain, initialState, grainTypeName, services, _dbClient, _spClient, _options, _serializerSettings);
        }

        private async Task TryDeleteDatabase()
        {
            try
            {
                var dbUri = UriFactory.CreateDatabaseUri(_options.DatabaseName);
                await _dbClient.ReadDatabaseAsync(dbUri);
                await _dbClient.DeleteDatabaseAsync(dbUri);
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task TryCreateDatabaseResources()
        {
            await _dbClient.CreateDatabaseIfNotExistsAsync(new Database { Id = _options.DatabaseName });

            var clusterCollection = new DocumentCollection
            {
                Id = _options.CollectionName
            };

            var partitionName = Char.ToLowerInvariant(_options.PartitionKey[0]) + _options.PartitionKey.Substring(1);
            clusterCollection.PartitionKey.Paths.Add($"/{partitionName}");
            // TODO: Set indexing policy to the collection

            await _dbClient.CreateDocumentCollectionIfNotExistsAsync(
                UriFactory.CreateDatabaseUri(_options.DatabaseName),
                clusterCollection,
                new RequestOptions
                {
                    //TODO: Check the consistency level for the emulator
                    //ConsistencyLevel = ConsistencyLevel.Strong,
                    OfferThroughput = _options.Throughput
                });

            try
            {                
                var masterDocumentResponse = await _dbClient.ReadDocumentAsync<MasterCommit>(UriFactory.CreateDocumentUri(_options.DatabaseName, _options.CollectionName, "0"), new RequestOptions { PartitionKey = new PartitionKey("commit") });
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    var masterCommit = new MasterCommit
                    {
                        Number = 0
                    };

                    await _dbClient.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(_options.DatabaseName, _options.CollectionName), masterCommit, new RequestOptions {PartitionKey = new PartitionKey("commit") });
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task UpdateServerSideLogic()
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var sproc in ServerSideRegistry.StoredProcedures.Files.Keys) 
            {
                using (var fileStream = assembly.GetManifestResourceStream($"Orleans.EventSourcing.EventStorage.StoredProcedures.{ServerSideRegistry.StoredProcedures.Files[sproc]}"))
                using (var reader = new StreamReader(fileStream))
                {
                    var content = await reader.ReadToEndAsync();
                    await TryUpdateStoredProcedure(sproc, content);
                }
            }

            foreach(var preTrigger in ServerSideRegistry.PreTriggers.Files.Keys)
            {
                using (var fileStream = assembly.GetManifestResourceStream($"Orleans.EventSourcing.EventStorage.Triggers.{ServerSideRegistry.PreTriggers.Files[preTrigger]}"))
                using (var reader = new StreamReader(fileStream))
                {
                    var content = await reader.ReadToEndAsync();
                    await TryUpdateTrigger(preTrigger, content, TriggerType.Pre, TriggerOperation.All);
                }
            }

            foreach (var postTrigger in ServerSideRegistry.PostTriggers.Files.Keys)
            {
                using (var fileStream = assembly.GetManifestResourceStream($"Orleans.EventSourcing.EventStorage.Triggers.{ServerSideRegistry.PostTriggers.Files[postTrigger]}"))
                using (var reader = new StreamReader(fileStream))
                {
                    var content = await reader.ReadToEndAsync();
                    await TryUpdateTrigger(postTrigger, content, TriggerType.Post, TriggerOperation.All);
                }
            }
        }

        private async Task TryUpdateStoredProcedure(string name, string content)
        {
            // Partitioned Collections do not support upserts, so check if they exist, and delete/re-insert them if they've changed.
            var insertStoredProc = false;

            try
            {
                var storedProcUri = UriFactory.CreateStoredProcedureUri(_options.DatabaseName, _options.CollectionName, name);
                var storedProcResponse = await _dbClient.ReadStoredProcedureAsync(storedProcUri);
                var storedProc = storedProcResponse.Resource;

                if (storedProc == null || !Equals(storedProc.Body, content))
                {
                    insertStoredProc = true;
                    await _dbClient.DeleteStoredProcedureAsync(storedProcUri);
                }
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    insertStoredProc = true;
                }
                else
                {
                    throw;
                }
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, $"Failure Updating Stored Procecure {name}");
                throw;
            }

            if (insertStoredProc)
            {
                var newStoredProc = new StoredProcedure()
                {
                    Id = name,
                    Body = content
                };

                await _dbClient.CreateStoredProcedureAsync(UriFactory.CreateDocumentCollectionUri(_options.DatabaseName, _options.CollectionName), newStoredProc);
            }
        }

        private async Task TryUpdateTrigger(string name, string content, TriggerType triggerType, TriggerOperation triggerOperation )
        {
            // Partitioned Collections do not support upserts, so check if they exist, and delete/re-insert them if they've changed.
            var insertTrigger = false;

            try
            {
                var triggerUri = UriFactory.CreateTriggerUri(_options.DatabaseName, _options.CollectionName, name);
                var triggerResponse = await _dbClient.ReadTriggerAsync(triggerUri);
                var trigger = triggerResponse.Resource;

                if (trigger == null || !Equals(trigger.Body, content))
                {
                    insertTrigger = true;
                    await _dbClient.DeleteTriggerAsync(triggerUri);
                }
            }
            catch (DocumentClientException dce)
            {
                if (dce.StatusCode == HttpStatusCode.NotFound)
                {
                    insertTrigger = true;
                }
                else
                {
                    throw;
                }
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, $"Failure Updating Trigger {name}");
                throw;
            }

            if (insertTrigger)
            {
                var trigger = new Trigger()
                {
                    Id = name,
                    Body = content,
                    TriggerType = triggerType,
                    TriggerOperation = triggerOperation
                };

                await _dbClient.CreateTriggerAsync(UriFactory.CreateDocumentCollectionUri(_options.DatabaseName, _options.CollectionName), trigger);
            }
        }
    }
}
