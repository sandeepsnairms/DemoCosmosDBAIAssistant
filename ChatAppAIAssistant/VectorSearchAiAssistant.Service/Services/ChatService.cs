using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using VectorSearchAiAssistant.Service.Constants;
using VectorSearchAiAssistant.Service.Interfaces;
using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Models.Search;
using VectorSearchAiAssistant.Service.Utils;

namespace VectorSearchAiAssistant.Service.Services;

public class ChatService : IChatService
{
    private readonly ICosmosDbService _cosmosDbService;
    private readonly IRAGService _ragService;
    private readonly ILogger _logger;

    public string Status
    {
        get
        {
            if (_cosmosDbService.IsInitialized && _ragService.IsInitialized)
                return "ready";

            var status = new List<string>();

            if (!_cosmosDbService.IsInitialized)
                status.Add("CosmosDBService: initializing");
            if (!_ragService.IsInitialized)
                status.Add("SemanticKernelRAGService: initializing");

            return string.Join(",", status);
        }
    }

    public ChatService(
        ICosmosDbService cosmosDbService,
        IRAGService ragService,
        ILogger<ChatService> logger)
    {
        _cosmosDbService = cosmosDbService;
        _ragService = ragService;
        _logger = logger;
    }

    /// <summary>
    /// Returns list of chat session ids and names.
    /// </summary>
    public async Task<List<Session>> GetAllChatSessionsAsync(PartitionManager.RecordQueryParams rParams)
    {
        return await _cosmosDbService.GetSessionsAsync(rParams);
    }

    /// <summary>
    /// Search the Chats sesseion  that meet search criteria
    /// </summary>
    public async Task<List<Session>> GetSessionsBySearchAsync(PartitionManager.RecordQueryParams rParams,string searchString)
    {
        return await _cosmosDbService.GetSessionsBySearchAsync(rParams,searchString);
    }
    


    /// <summary>
    /// Returns the chat messages related to an existing session.
    /// </summary>
    public async Task<List<Message>> GetChatSessionMessagesAsync(PartitionManager.RecordQueryParams rParams, DateTime oldestMessageTimestamp)
    {
        ArgumentNullException.ThrowIfNull(rParams);
        return await _cosmosDbService.GetSessionExistingMessagesAsync(rParams, oldestMessageTimestamp);
    }


    /// <summary>
    /// Returns the chat messages related to an existing session.
    /// </summary>
    public async Task<List<Message>> GetChatSessionNewMessagesAsync(PartitionManager.RecordQueryParams rParams, DateTime latestMessageTimestamp)
    {
        ArgumentNullException.ThrowIfNull(rParams);
        return await _cosmosDbService.GetSessionMessageUpdatesAsync(rParams, latestMessageTimestamp);
    }

    /// <summary>
    /// Creates a new chat session.
    /// </summary>
    public async Task<Session> CreateNewChatSessionAsync(PartitionManager.RecordQueryParams rParams)
    {
        Session session = new(rParams.tenantId,rParams.userId);

        rParams.pk = session.SessionId;

        return await _cosmosDbService.InsertSessionAsync(rParams,session);
    }

    /// <summary>
    /// Rename the chat session from its default (eg., "New Chat") to the summary provided by OpenAI.
    /// </summary>
    public async Task<Session> RenameChatSessionAsync(PartitionManager.RecordQueryParams rParams, string newChatSessionName)
    {
        ArgumentNullException.ThrowIfNull(rParams);
        ArgumentException.ThrowIfNullOrEmpty(newChatSessionName);

        return await _cosmosDbService.UpdateSessionNameAsync(rParams, newChatSessionName);
    }

    /// <summary>
    /// Delete a chat session and related messages.
    /// </summary>
    public async Task DeleteChatSessionAsync(PartitionManager.RecordQueryParams rParams)
    {
        ArgumentNullException.ThrowIfNull(rParams);
        await _cosmosDbService.DeleteSessionAndMessagesAsync(rParams);
    }

    /// <summary>
    /// Receive a prompt from a user, vectorize it from the OpenAI service, and get a completion from the OpenAI service.
    /// </summary>
    public async Task<Completion> GetChatCompletionAsync(PartitionManager.RecordQueryParams rParams, string userPrompt)
    {        


        try
        {
            ArgumentNullException.ThrowIfNull(rParams);

            // Retrieve conversation, including latest prompt.
            // If you put this after the vector search it doesn't take advantage of previous information given so harder to chain prompts together.
            // However if you put this before the vector search it can get stuck on previous answers and not pull additional information. Worth experimenting

            // Retrieve conversation, including latest prompt.
            var messages = await _cosmosDbService.GetSessionExistingMessagesAsync(rParams, DateTime.Now);

            // Generate the completion to return to the user
            //(string completion, int promptTokens, int responseTokens) = await openAiService.GetChatCompletionAsync(rParams, conversation, retrievedDocuments);
            var result = await _ragService.GetResponse(userPrompt, messages);

            // Add both prompt and completion to cache, then persist in Cosmos DB
            var promptMessage = new Message(rParams.tenantId,rParams.userId,rParams.documentId, nameof(Participants.User), result.UserPromptTokens, userPrompt, result.UserPromptEmbedding, null);
            var completionMessage = new Message(rParams.tenantId, rParams.userId, rParams.documentId, nameof(Participants.Assistant), result.ResponseTokens, result.Completion, null, null);

            var completionPrompt = new CompletionPrompt(rParams.tenantId, rParams.userId, rParams.documentId, completionMessage.Id, result.UserPrompt);
            completionMessage.CompletionPromptId = completionPrompt.Id;

            await AddPromptCompletionMessagesAsync(rParams, promptMessage, completionMessage, completionPrompt);

            return new Completion { Text = result.Completion };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting completion in session {rParams} for user prompt [{userPrompt}].");
            return new Completion { Text = "Could not generate a completion due to an internal error." };
        }
    }

    /// <summary>
    /// Generate a name for a chat message, based on the passed in prompt.
    /// </summary>
    public async Task<Completion> SummarizeChatSessionNameAsync(PartitionManager.RecordQueryParams rParams, string prompt)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            await Task.CompletedTask;

            var summary = await _ragService.Summarize(rParams.documentId, prompt);

            await RenameChatSessionAsync(rParams, summary);

            return new Completion { Text = summary };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting a summary in session {rParams} for user prompt [{prompt}].");
            return new Completion { Text = "[No Summary]" };
        }
    }

    /// <summary>
    /// Add a new user prompt to the chat session and insert into the data service.
    /// </summary>
    private async Task<Message> AddPromptMessageAsync(PartitionManager.RecordQueryParams rParams, string promptText)
    {
        Message promptMessage = new(rParams.tenantId,rParams.userId,rParams.pk, nameof(Participants.User), default, promptText, null, null);

        return await _cosmosDbService.InsertMessageAsync(rParams, promptMessage);
    }


    /// <summary>
    /// Add user prompt and AI assistance response to the chat session message list object and insert into the data service as a transaction.
    /// </summary>
    private async Task AddPromptCompletionMessagesAsync(PartitionManager.RecordQueryParams rParams, Message promptMessage, Message completionMessage, CompletionPrompt completionPrompt)
    {
        var session = await _cosmosDbService.GetSessionAsync(rParams);

        // Update session cache with tokens used
        session.TokensUsed += promptMessage.Tokens;
        session.TokensUsed += completionMessage.Tokens;

        await _cosmosDbService.UpsertSessionBatchAsync(rParams, session, promptMessage, completionMessage, completionPrompt);
    }

    /// <summary>
    /// Rate an assistant message. This can be used to discover useful AI responses for training, discoverability, and other benefits down the road.
    /// </summary>
    public async Task<Message> RateMessageAsync(PartitionManager.RecordQueryParams rParams, bool? rating)
    {
        ArgumentNullException.ThrowIfNull(rParams);

        return await _cosmosDbService.UpdateMessageRatingAsync(rParams, rating);
    }

    
    public async Task<CompletionPrompt> GetCompletionPrompt(PartitionManager.RecordQueryParams rParams)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(rParams.documentId);
 
        return await _cosmosDbService.GetCompletionPrompt(rParams);
    }
}