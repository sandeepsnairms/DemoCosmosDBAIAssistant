using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Core;
using Microsoft.Extensions.Logging;
using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Interfaces;
using VectorSearchAiAssistant.Service.Models.Search;
using Microsoft.Extensions.Options;
using VectorSearchAiAssistant.Service.Models.ConfigurationOptions;
using Newtonsoft.Json.Linq;
using VectorSearchAiAssistant.Service.Models;
using VectorSearchAiAssistant.Service.Utils;
using System.Diagnostics;
using Castle.Core.Resource;
using VectorSearchAiAssistant.SemanticKernel.Models;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Azure;
using System.ComponentModel;
using NUnit.Framework;
using Container = Microsoft.Azure.Cosmos.Container;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;
using System.Xml.Linq;
using System;
using System.Security.Cryptography;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Schemas;
using System.Collections.Concurrent;
using System.Security.Policy;
using Azure.Core;
using System.Collections.Specialized;
using System.Linq.Expressions;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Message = VectorSearchAiAssistant.Service.Models.Chat.Message;
using System.Diagnostics.Eventing.Reader;
using static Azure.Core.HttpHeader;
using System.Drawing;
using System.Text.RegularExpressions;

namespace VectorSearchAiAssistant.Service.Services
{
    /// <summary>
    /// Service to access Azure Cosmos DB for NoSQL.
    /// </summary>
    public class CosmosDbService : ICosmosDbService
    {
        private readonly Container _completions;
        private readonly Container _guides;
        private readonly Container _leases;
        private readonly Container _metrics;
        private readonly Database _database;
        private readonly Dictionary<string, Container> _containers;
        readonly Dictionary<string, Type> _memoryTypes;

        private readonly IRAGService _ragService;
        private readonly ICognitiveSearchService _cognitiveSearchService;
        private readonly CosmosDbSettings _settings;
        private readonly ILogger _logger;

        private List<ChangeFeedProcessor> _changeFeedProcessors;
        private bool _changeFeedsInitialized = false;

        private bool _isPKHierarchical;

        public bool IsInitialized => _changeFeedsInitialized;

        public CosmosDbService(
            IRAGService ragService,
            ICognitiveSearchService cognitiveSearchService,
            IOptions<CosmosDbSettings> settings, 
            ILogger<CosmosDbService> logger)
        {
            _ragService = ragService;
            _cognitiveSearchService = cognitiveSearchService;

            _settings = settings.Value;
            ArgumentException.ThrowIfNullOrEmpty(_settings.Endpoint);
            ArgumentException.ThrowIfNullOrEmpty(_settings.Key);
            ArgumentException.ThrowIfNullOrEmpty(_settings.Database);
            ArgumentException.ThrowIfNullOrEmpty(_settings.Containers);

            if(_settings.CompletionContainer.EndsWith("_hpk"))
                _isPKHierarchical = true;

            _logger = logger;

            _logger.LogInformation("Initializing Cosmos DB service.");

            if (!_settings.EnableTracing)
            {
                Type defaultTrace = Type.GetType("Microsoft.Azure.Cosmos.Core.Trace.DefaultTrace,Microsoft.Azure.Cosmos.Direct");
                TraceSource traceSource = (TraceSource)defaultTrace.GetProperty("TraceSource").GetValue(null);
                traceSource.Switch.Level = SourceLevels.All;
                traceSource.Listeners.Clear();
            }


            // Configure CosmosClientOptions
            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    IgnoreNullValues = true,
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                },
                ApplicationName = "ChatAPI-" + Environment.MachineName,
                ConnectionMode = ConnectionMode.Direct,
                ApplicationPreferredRegions = _settings.ApplicationPreferredRegions.Split(',').ToList(),
                MaxRetryAttemptsOnRateLimitedRequests = 0,
                
                
                
               
            };

            // Create a CosmosClient using the endpoint and key
            CosmosClient client = new CosmosClient(_settings.Endpoint, _settings.Key, clientOptions);

            Database? database = client?.GetDatabase(_settings.Database);

            _database = database ??
                        throw new ArgumentException("Unable to connect to existing Azure Cosmos DB database.");


            //Dictionary of container references for all containers listed in config
            _containers = new Dictionary<string, Container>();

            List<string> containers = _settings.Containers.Split(',').ToList();

            foreach (string containerName in containers)
            {
                Container? container = database?.GetContainer(containerName.Trim()) ??
                                       throw new ArgumentException("Unable to connect to existing Azure Cosmos DB container or database.");

                _containers.Add(containerName.Trim(), container);
            }

            _completions = _containers[_settings.CompletionContainer];
            _guides = _containers["guides"];

            _metrics = database?.GetContainer(_settings.MetricsContainer)
                 ?? throw new ArgumentException($"Unable to connect to the {_settings.MetricsContainer} container required to store the CosmosDB metrics.");

            _leases = database?.GetContainer(_settings.ChangeFeedLeaseContainer)
                ?? throw new ArgumentException($"Unable to connect to the {_settings.ChangeFeedLeaseContainer} container required to listen to the CosmosDB change feed.");

            _memoryTypes = RAGModelRegistry.Models.ToDictionary(m => m.Key, m => m.Value.Type);

            Task.Run(() => StartChangeFeedProcessors());
            _logger.LogInformation("Cosmos DB service initialized.");
        }

        private async Task StartChangeFeedProcessors()
        {

            _logger.LogInformation("Initializing the Cognitive Search index...");
            await _cognitiveSearchService.Initialize(_memoryTypes.Values.ToList());

            _logger.LogInformation("Initializing the change feed processors...");
            _changeFeedProcessors = new List<ChangeFeedProcessor>();

            try
            {

                foreach (string monitoredContainerName in _settings.MonitoredContainers.Split(',').Select(s => s.Trim()))
                {
                    var changeFeedProcessor = _containers[monitoredContainerName]
                        .GetChangeFeedProcessorBuilder<dynamic>($"{monitoredContainerName}ChangeFeed", GenericChangeFeedHandler)
                        .WithInstanceName($"{monitoredContainerName}ChangeInstance")
                        .WithErrorNotification(GenericChangeFeedErrorHandler)
                        .WithStartTime(DateTime.MinValue.ToUniversalTime())
                        .WithLeaseContainer(_leases)
                        .Build();
                    await changeFeedProcessor.StartAsync();
                    _changeFeedProcessors.Add(changeFeedProcessor);
                    _logger.LogInformation($"Initialized the change feed processor for the {monitoredContainerName} container.");
                }

                _changeFeedsInitialized = true;
                _logger.LogInformation("Cosmos DB change feed processors initialized.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing change feed processors.");
            }
        }

        // This is an example of a dynamic change feed handler that can handle a range of preconfigured entities.
        private async Task GenericChangeFeedHandler(
            ChangeFeedProcessorContext context,
            IReadOnlyCollection<dynamic> changes,
            CancellationToken cancellationToken)
        {
            if (changes.Count == 0)
                return;

            var batchRef = Guid.NewGuid().ToString();
            _logger.LogInformation($"Starting to generate embeddings for {changes.Count} entities (batch ref {batchRef}).");

            // Using dynamic type as this container has two different entities
            foreach (var item in changes)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var jObject = item as JObject;
                    var typeMetadata = RAGModelRegistry.IdentifyType(jObject);

                    if (typeMetadata == null)
                    {
                        //Use custome logic for Change feed
                        
                    }
                    else
                    {
                        // Use RAG pattern, vectorize data, add to index

                        var entity = jObject.ToObject(typeMetadata.Type);                       

                        // Add the entity to the Cognitive Search content index
                        // The content index is used by the Cognitive Search memory source to run create memories from faceted queries
                        await _cognitiveSearchService.IndexItem(entity);

                        // Add the entity to the Semantic Kernel memory used by the RAG service
                        
                        await _ragService.AddMemory(
                            entity,
                            string.Join(" ", entity.GetPropertyValues(typeMetadata.NamingProperties)));
                        
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing an item in the change feed handler: {item}");
                }
            }

            _logger.LogInformation($"Finished generating embeddings (batch ref {batchRef}).");
        }

        private async Task GenericChangeFeedErrorHandler(
            string LeaseToken,
            Exception exception)
        {
            if (exception is ChangeFeedProcessorUserException userException)
            {
                Console.WriteLine($"Lease {LeaseToken} processing failed with unhandled exception from user delegate {userException.InnerException}");
            }
            else
            {
                Console.WriteLine($"Lease {LeaseToken} failed with {exception}");
            }

            await Task.CompletedTask;
        }

        #region Helper Functions




        private async Task<List<T>> ExecuteQueryAsync<T>(Container container, QueryDefinition query, PartitionManager.RecordQueryParams rParams, string name)
        {
            var metricLog = new CosmosMetricStore(name,_metrics);

            try
            {
                FeedIterator<T> results;

                Microsoft.Azure.Cosmos.PartitionKey partitionKey = PartitionManager.GetPK(rParams,_isPKHierarchical);

                if (partitionKey == PartitionKey.Null)
                    results = container.GetItemQueryIterator<T>(query);
                else
                    results = container.GetItemQueryIterator<T>(query, null, new QueryRequestOptions() { PartitionKey = partitionKey });

 
                List<T> output = new();
                while (results.HasMoreResults)
                {
                    FeedResponse<T> response = await results.ReadNextAsync();
                                        
                    metricLog.UpdateResponse<T>(response);

                    output.AddRange(response);
                }

                metricLog.StoreSucessMetric();

                return output;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);

                return new List<T>();
            }

        }

        private async Task<T> ReadItemAsync<T>(Container container, PartitionManager.RecordQueryParams rParams, string name) 
        {
            
            var metricLog = new CosmosMetricStore(name,_metrics);
            try
            {

                var response = await container.ReadItemAsync<T>(
                    id: rParams.documentId,
                    partitionKey: PartitionManager.GetPK(rParams,_isPKHierarchical));
                
                metricLog.UpdateResponse<T>(response);                                             

                metricLog.StoreSucessMetric();
                return response;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);                

                return default(T);
            }
        }

        private async Task<T> InsertItemAsync<T>( Container container, PartitionManager.RecordQueryParams rParams, T item, string name, bool Upsert = false)
        {            

            var metricLog = new CosmosMetricStore(name,_metrics);
            try
            {     
                ItemResponse<T> response;
                if (Upsert)
                {
                    response = await container.UpsertItemAsync(
                        item: item,
                        partitionKey: PartitionManager.GetPK(rParams,_isPKHierarchical)
                    );
                }
                else
                {
                    response = await container.CreateItemAsync(
                        item: item,
                        partitionKey: PartitionManager.GetPK(rParams,_isPKHierarchical)
                    );
                }

                
                metricLog.UpdateResponse<T>(response);

                metricLog.StoreSucessMetric();

                return response;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);

                return default(T);
            }
        }

        

        private async Task<T> ReplaceItemAsync<T>(Container container, PartitionManager.RecordQueryParams rParams, T item, string  name)
        {

            var metricLog = new CosmosMetricStore(name,_metrics);
            try
            {
                var response = await container.ReplaceItemAsync(
                    item: item,
                    id: rParams.documentId,
                    partitionKey: PartitionManager.GetPK(rParams,_isPKHierarchical)
                );

                metricLog.UpdateResponse<T>(response);

                metricLog.StoreSucessMetric();

                return response;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);

                return default(T);
            }
        }


        private async Task<T> PathItemAsync<T>(Container container, PartitionManager.RecordQueryParams rParams, PatchOperation[] patchOperations, string name)
        {
            var metricLog = new CosmosMetricStore(name,_metrics);
            try
            {              
                var response = await container.PatchItemAsync<T>(
                    id: rParams.documentId,
                    partitionKey: PartitionManager.GetPK(rParams,_isPKHierarchical),
                    patchOperations: patchOperations
                );
                  
                metricLog.UpdateResponse<T>(response);

                metricLog.StoreSucessMetric();

                return response.Resource;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);

                return default(T);
            }
        }


        private async Task<ItemResponse<T>> DeleteItemAsync<T>(Container container, PartitionManager.RecordQueryParams rParams, string name)
        {
            
            var metricLog = new CosmosMetricStore(name,_metrics);
            try
            {

                var response = await container.DeleteItemAsync<T>(rParams.documentId, PartitionManager.GetPK(rParams,_isPKHierarchical));

                metricLog.UpdateResponse<T>(response);

                metricLog.StoreSucessMetric();

                return response;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + name);
                metricLog.StoreExceptionMetric(ex);

                return null;
            }
    }

        #endregion


        /// <summary>
        /// Gets a list of all current chat sessions.
        /// </summary>
        /// <returns>List of distinct chat session items.</returns>
        public async Task<List<Session>> GetSessionsAsync(PartitionManager.RecordQueryParams rParams)
        {
                QueryDefinition query = new QueryDefinition("SELECT DISTINCT * FROM c WHERE c.type = @type and c.tenantId=@tenant and c.userId=@user order by c.timeStamp desc")
                    .WithParameter("@tenant", rParams.tenantId)
                    .WithParameter("@user", rParams.userId)
                    .WithParameter("@type", nameof(Session));

                var output = await ExecuteQueryAsync<Session>(_completions, query, rParams, "Get All Sessions");
                return output;           
        }


        /// <summary>
        ///  Search the Chats sesseion  that meet search criteria
        /// </summary>
        /// <returns>List of distinct chat session items.</returns>
        public async Task<List<Session>> GetSessionsBySearchAsync(PartitionManager.RecordQueryParams rParams, string searchString)
        {

            QueryDefinition query = new QueryDefinition($"SELECT distinct value c.sessionId FROM c WHERE  c.tenantId=@tenant and c.userId=@user and c.timeStamp >=@timeStamp and  CONTAINS(c.text, '{searchString}', true) or CONTAINS(c.name, '{searchString}', true)  order by c.timeStamp desc")
                .WithParameter("@tenant", rParams.tenantId)
                .WithParameter("@user", rParams.userId)
                .WithParameter("@timeStamp", System.DateTime.Now.AddDays(-30))
                .WithParameter("@searchString", searchString);


            var output = await ExecuteQueryAsync<string>(_completions, query, rParams, "Search Messages");

            if (output.Count > 0)
                return await GetSessionsFromIdListAsync(output, rParams);
            else
                return new List<Session>();
        }

        private async Task<List<Session>> GetSessionsFromIdListAsync(List<string> listOfIds, PartitionManager.RecordQueryParams rParams)
        {

                QueryDefinition query = new QueryDefinition($"SELECT * FROM c WHERE c.type = @type and c.tenantId=@tenant and c.userId=@user and c.sessionId IN  ({string.Join(",", listOfIds.Select(id => $"'{id}'"))})")
                    .WithParameter("@tenant", rParams.tenantId)
                    .WithParameter("@user", rParams.userId)
                    .WithParameter("@type", nameof(Session));

                var output = await ExecuteQueryAsync<Session>(_completions, query, rParams, "Get Sessions By Search");
                return output;

        }

        /// <summary>
        /// Performs a point read to retrieve a single chat session item.
        /// </summary>
        /// <returns>The chat session item.</returns>
        public async Task<Session> GetSessionAsync(PartitionManager.RecordQueryParams rParams) 
        {
            return await ReadItemAsync< Session>(_completions, rParams, "Get Session- ReadItem");
        }

        /// <summary>
        /// Performs a point read to retrieve a completion item.
        /// </summary>
        /// <returns>The completion item.</returns>
        public async Task<CompletionPrompt> GetCompletionPrompt(PartitionManager.RecordQueryParams rParams)
        {
            return await ReadItemAsync<CompletionPrompt>(_completions, rParams, "Get Completion Prompt");

        }

        /// <summary>
        /// Gets a list of all current chat messages for a specified session identifier.
        /// </summary>
        /// <param name="sessionId">Chat session identifier used to filter messsages.</param>
        /// <returns>List of chat message items for the specified session.</returns>
        public async Task<List<Message>> GetSessionExistingMessagesAsync(PartitionManager.RecordQueryParams rParams, DateTime oldestMessageTimestamp)
        {

            QueryDefinition query;

            if (rParams.enablePaging)
            {
                //query = new QueryDefinition("SELECT  TOP @limit * FROM c WHERE c.sessionId = @sessionId AND c.type = @type AND c.timeStamp < @oldestMessageTimestamp order by c._ts desc")
                query= new QueryDefinition("SELECT * FROM c WHERE c.sessionId = @sessionId AND c.type = @type AND c.timeStamp < @oldestMessageTimestamp order by c._ts desc OFFSET 0 LIMIT @limit")
                       .WithParameter("@oldestMessageTimestamp", oldestMessageTimestamp)
                       .WithParameter("@limit", _settings.MaxMessagesPerPage)
                       .WithParameter("@sessionId", rParams.documentId)
                       .WithParameter("@type", nameof(Message));
            }
            else
            {
                query = new QueryDefinition("SELECT * FROM c WHERE c.sessionId = @sessionId AND c.type = @type order by c._ts  desc")
                       .WithParameter("@sessionId", rParams.documentId)
                       .WithParameter("@type", nameof(Message));
            }

            var output = await ExecuteQueryAsync<Message>(_completions, query, rParams, "Get Session Existing Messages");

            //reversing the order  as query had desc results            
            List<Message> reversedResults = output.OrderBy(x => x.TimeStamp).ToList();
            return reversedResults;
        }

        /// <summary>
        /// Gets a list of all new chat messages for a specified session identifier.
        /// </summary>
        /// <param name="sessionId">Chat session identifier used to filter messsages.</param>
        /// <returns>List of chat message items for the specified session.</returns>
        public async Task<List<Message>> GetSessionMessageUpdatesAsync(PartitionManager.RecordQueryParams rParams, DateTime latestMessageTimestamp)
        {

            QueryDefinition query =
                new QueryDefinition("SELECT * FROM c WHERE c.sessionId = @sessionId AND c.type = @type AND c.timeStamp > @latestMessageTimestamp order by c._ts")
                    .WithParameter("@latestMessageTimestamp", latestMessageTimestamp)
                    .WithParameter("@sessionId", rParams.documentId)
                    .WithParameter("@type", nameof(Message));

            var output = await ExecuteQueryAsync<Message>(_completions, query, rParams, "Get Session Message Updates");
            return output;
        }

        /// <summary>
        /// Creates a new chat session.
        /// </summary>
        /// <param name="session">Chat session item to create.</param>
        /// <returns>Newly created chat session item.</returns>
        public async Task<Session> InsertSessionAsync(PartitionManager.RecordQueryParams rParams, Session session)
        {
            return await InsertItemAsync< Session>(_completions, rParams, session, "Insert Session");
        }

        /// <summary>
        /// Creates a new chat message.
        /// </summary>
        /// <param name="message">Chat message item to create.</param>
        /// <returns>Newly created chat message item.</returns>
        public async Task<Message> InsertMessageAsync(PartitionManager.RecordQueryParams rParams, Message message)
        {
            return await InsertItemAsync<Message>(_completions, rParams, message, "Insert Message");          
        }

        private async Task UpsertSessionTokensAsync(Session session, PartitionManager.RecordQueryParams rParams)
        {
            await InsertItemAsync<Session>(_completions, rParams, session, "Upsert Session Tokens", true);

        }

        /// <summary>
        /// Updates an existing chat message.
        /// </summary>
        /// <param name="message">Chat message item to update.</param>
        /// <returns>Revised chat message item.</returns>
        public async Task<Message> UpdateMessageAsync(PartitionManager.RecordQueryParams rParams, Message message)
        {
            return await ReplaceItemAsync<Message>(_completions, rParams, message, "Update Message");
      
        }        

        /// <summary>
        /// Updates an existing chat session.
        /// </summary>
        /// <param name="session">Chat session item to update.</param>
        /// <returns>Revised created chat session item.</returns>
        public async Task<Session> UpdateSessionAsync(PartitionManager.RecordQueryParams rParams, Session session)
        {

            return await ReplaceItemAsync<Session>(_completions, rParams, session, "Update Session");

        }

        /// <summary>
        /// Updates a session's name through a patch operation.
        /// </summary>
        /// <param name="id">The session id.</param>
        /// <param name="name">The session's new name.</param>
        /// <returns>Revised chat session item.</returns>
        public async Task<Session> UpdateSessionNameAsync(PartitionManager.RecordQueryParams rParams,string name)//id
        {
            PatchOperation[] patchOperations = new[]
                    {
                        PatchOperation.Set("/name", name),
                    };

            return await PathItemAsync<Session>(_completions, rParams, patchOperations, "Update Session Name");            
        }


        /// <summary>
        /// Updates a message's rating through a patch operation.
        /// </summary>
        /// <param name="id">The message id.</param>
        /// <param name="sessionId">The message's partition key (session id).</param>
        /// <param name="rating">The rating to replace.</param>
        /// <returns>Revised chat message item.</returns>
        public async Task<Message> UpdateMessageRatingAsync(PartitionManager.RecordQueryParams rParams, bool? rating)//id,sessionId
        {

            PatchOperation[] patchOperations = new[]
                    {
                        PatchOperation.Set("/rating", rating),
                    };

            return await PathItemAsync<Message>(_completions, rParams, patchOperations, "Update Message Rating");
            
        }

        /// <summary>
        /// Batch create or update chat messages and session.
        /// </summary>
        /// <param name="messages">Chat message and session items to create or replace.</param>
        public async Task UpsertSessionBatchAsync(PartitionManager.RecordQueryParams rParams, Session session, params dynamic[] messages)
        {
            if (messages.Select(m => m.SessionId).Distinct().Count() > 1)
            {
                throw new ArgumentException("All items must have the same partition key.");
            }            

            var metricLog = new CosmosMetricStore("Upsert Session Messages Batch", _metrics);
            try
            {
                
                var batch = _completions.CreateTransactionalBatch(PartitionManager.GetPK(rParams,_isPKHierarchical));
                foreach (var message in messages)
                {
                    batch.UpsertItem(
                        item: message
                    );
                }

                var response = await batch.ExecuteAsync();

                metricLog.UpdateResponse(response);

                metricLog.StoreSucessMetric();

                await UpsertSessionTokensAsync(session, rParams);
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + "Upsert Session Messages Batch");
                metricLog.StoreExceptionMetric(ex);
            }
        }


        /// <summary>
        /// Batch deletes an existing chat session and all related messages.
        /// </summary>
        /// <param name="sessionId">Chat session identifier used to flag messages and sessions for deletion.</param>
        public async Task DeleteSessionAndMessagesAsync(PartitionManager.RecordQueryParams rParams)//sessionId
        {
    
            // TODO: Used SDK version 3.25.0-preview  then await container.DeleteAllItemsByPartitionKeyStreamAsync(partitionKey);

            var query = new QueryDefinition("SELECT c.id FROM c WHERE c.sessionId = @sessionId")
                .WithParameter("@sessionId", rParams.documentId);

            bool sessionDeleted = false;

  
            var metricLog = new CosmosMetricStore("Delete Session Messages", _metrics);

            try
            {
                
                var response = _completions.GetItemQueryIterator<Message>(query, null, new QueryRequestOptions() { PartitionKey = PartitionManager.GetPK(rParams,_isPKHierarchical) });

                var batch = _completions.CreateTransactionalBatch(PartitionManager.GetPK(rParams,_isPKHierarchical));
                while (response.HasMoreResults)
                {
                    var results = await response.ReadNextAsync();

                    foreach (var item in results)
                    {
                        //in case session and message have same pk then no need to delete seperately
                        if (item.Id==rParams.documentId)
                            sessionDeleted = true;


                        batch.DeleteItem(
                            id: item.Id
                        );
                    }
                }

                var batchresponse = await batch.ExecuteAsync();
                
                metricLog.UpdateResponse(batchresponse);

                metricLog.StoreSucessMetric();

                if (!sessionDeleted)
                    await DeleteItemAsync<Session>(_completions, rParams, "Delete Session");
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "Error in " + "Delete Session");
                metricLog.StoreExceptionMetric(ex);
            }            
            
        }

        
        /// <summary>
        /// Reads all documents retrieved by Vector Search.
        /// </summary>
        /// <param name="vectorDocuments">List string of JSON documents from vector search results</param>
        public async Task<string> GetVectorSearchDocumentsAsync( List<DocumentVector> vectorDocuments)
        {

            List<string> searchDocuments = new List<string>();

            foreach (var document in vectorDocuments)
            {

                try
                {
                    var response = await _containers[document.containerName].ReadItemStreamAsync(
                        document.itemId, new PartitionKey(document.partitionKey));


                    if ((int) response.StatusCode < 200 || (int) response.StatusCode >= 400)
                        _logger.LogError(
                            $"Failed to retrieve an item for id '{document.itemId}' - status code '{response.StatusCode}");

                    if (response.Content == null)
                    {
                        _logger.LogInformation(
                            $"Null content received for document '{document.itemId}' - status code '{response.StatusCode}");
                        continue;
                    }

                    string item;
                    using (StreamReader sr = new StreamReader(response.Content))
                        item = await sr.ReadToEndAsync();

                    searchDocuments.Add(item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message, ex);

                }
            }

            var resultDocuments = string.Join(Environment.NewLine + "-", searchDocuments);

            return resultDocuments;

        }       
        
    }

    

 
}
