using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Utils;

namespace Search.Helpers;

public interface IChatManager
{
    /// <summary>
    /// Returns list of chat session ids and names for left-hand nav to bind to (display Name and ChatSessionId as hidden)
    /// </summary>
    Task<List<Session>> GetAllChatSessionsAsync(PartitionManager.RecordQueryParams rParams);


    /// <summary>
    /// Returns list of chat session ids that match search criteria
    /// </summary>
    Task<List<Session>> GetSessionsBySearchAsync(PartitionManager.RecordQueryParams rParams,string searchString);


    /// <summary>
    /// Returns the chat messages to display on the main web page when the user selects a chat from the left-hand nav
    /// </summary>
    Task<List<Message>> GetChatSessionMessagesAsync(PartitionManager.RecordQueryParams rParams, DateTime oldestMessageTimestamp, bool renderFirst);

    /// <summary>
    /// Returns the new chat messages to display when user submits a prompt
    /// </summary>
    Task<List<Message>> GetChatSessionMessagesUpdatesAsync(PartitionManager.RecordQueryParams rParams, DateTime latestMessageTimestamp);

    /// <summary>
    /// User creates a new Chat Session.
    /// </summary>
    Task CreateNewChatSessionAsync(PartitionManager.RecordQueryParams rParams);

    /// <summary>
    /// Rename the Chat Ssssion from "New Chat" to the summary provided by OpenAI
    /// </summary>
    Task RenameChatSessionAsync(PartitionManager.RecordQueryParams rParams, string newChatSessionName, bool onlyUpdateLocalSessionsCollection = false);

    /// <summary>
    /// User deletes a chat session
    /// </summary>
    Task DeleteChatSessionAsync(PartitionManager.RecordQueryParams rParams);

    /// <summary>
    /// Receive a prompt from a user, Vectorize it from _openAIService Get a completion from _openAiService
    /// </summary>
    Task<string> GetChatCompletionAsync(PartitionManager.RecordQueryParams rParams, string userPrompt, DateTime latestMessageTimestamp);

    Task<string> SummarizeChatSessionNameAsync(PartitionManager.RecordQueryParams rParams, string prompt);

    /// <summary>
    /// Rate an assistant message. This can be used to discover useful AI responses for training, discoverability, and other benefits down the road.
    /// </summary>
    Task<Message> RateMessageAsync(PartitionManager.RecordQueryParams rParams, bool? rating);

    Task<CompletionPrompt> GetCompletionPrompt(PartitionManager.RecordQueryParams rParams);
}