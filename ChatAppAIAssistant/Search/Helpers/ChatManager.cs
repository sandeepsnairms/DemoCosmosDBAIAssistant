using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using VectorSearchAiAssistant.Service.Interfaces;
using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Utils;
using static VectorSearchAiAssistant.Service.Utils.PartitionManager;

namespace Search.Helpers
{
    public class ChatManager : IChatManager
    {
        /// <summary>
        /// All data is cached in the _sessions List object.
        /// </summary>
        private List<Session> _sessions { get; set; }

        private readonly ChatManagerSettings _settings;
        private HttpClient _httpClient;

        public ChatManager(
            IOptions<ChatManagerSettings> settings)
        {
            _settings = settings.Value;

            _httpClient = new HttpClient()
            {
                BaseAddress = new Uri(_settings.APIUrl)
            };
        }

        private string serialize(PartitionManager.RecordQueryParams rParams)
        {
            return JsonConvert.SerializeObject(rParams);
        }

        /// <summary>
        /// Returns list of chat session ids and names for left-hand nav to bind to (display Name and ChatSessionId as hidden)
        /// </summary>
        public async Task<List<Session>> GetAllChatSessionsAsync(PartitionManager.RecordQueryParams rParams)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            string serialized_rId = serialize(rParams);

            _sessions = await SendRequest<List<Session>>(HttpMethod.Get, $"/sessions/{serialized_rId}");

            return _sessions;
        }

        /// <summary>
        /// Returns list of chat session ids and names for left-hand nav to bind to (display Name and ChatSessionId as hidden)
        /// </summary>
        public async Task<List<Session>> GetSessionsBySearchAsync(PartitionManager.RecordQueryParams rParams, string searchString)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            string serialized_rId = serialize(rParams);
            _sessions = await SendRequest<List<Session>>(HttpMethod.Post, $"/sessions/{serialized_rId}/search?searchString={searchString}");
            return _sessions;
        }

        /// <summary>
        /// Returns the chat messages to display on the main web page when the user selects a chat from the left-hand nav
        /// </summary>
        public async Task<List<Message>> GetChatSessionMessagesAsync(PartitionManager.RecordQueryParams rParams, DateTime oldestMessageTimestamp, bool renderFirst)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            List<Message> chatMessages;

            if (_sessions.Count == 0)
            {
                return Enumerable.Empty<Message>().ToList();
            }

            var index = _sessions.FindIndex(s => s.SessionId == rParams.documentId);

            string serialized_rId = serialize(rParams);
            long ticks = oldestMessageTimestamp.Ticks;
            chatMessages = await SendRequest<List<Message>>(HttpMethod.Get, $"/sessions/{serialized_rId}/messages/{ticks}");

            // Cache results
            if (_sessions[index].Messages == null  || renderFirst == true || rParams.enablePaging==false)
            {
                _sessions[index].Messages = chatMessages;
                return chatMessages;
            }
            else
            {
                IEnumerable<Message> newItems = chatMessages; // Your new data
                List<Message> existingList = _sessions[index].Messages; // Your existing list

                IEnumerable<Message> combinedMessages = newItems.Concat(existingList);

                combinedMessages = combinedMessages.Distinct(new MessageEqualityComparer()).ToList();

                _sessions[index].Messages = combinedMessages.ToList<Message>();

                return _sessions[index].Messages; 
            }
            
        }


        /// <summary>
        /// Returns the new chat messages to display when user submits a prompt
        /// </summary>
        public async Task<List<Message>> GetChatSessionMessagesUpdatesAsync(PartitionManager.RecordQueryParams rParams, DateTime latestMessageTimestamp)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            List<Message> chatMessages;

            if (_sessions.Count == 0)
            {
                return Enumerable.Empty<Message>().ToList();
            }

            var index = _sessions.FindIndex(s => s.SessionId == rParams.documentId);

            string serialized_rId = serialize(rParams);
            long ticks = latestMessageTimestamp.Ticks;
            chatMessages = await SendRequest<List<Message>>(HttpMethod.Get, $"/sessions/{serialized_rId}/new_messages/{ticks}");

            IEnumerable<Message> newItems = chatMessages; // Your new data
            List<Message> existingList = _sessions[index].Messages; // Your existing list

            IEnumerable<Message> combinedMessages = existingList.Concat(newItems);

            _sessions[index].Messages = combinedMessages.ToList<Message>(); 

            return combinedMessages.ToList<Message>();// chatMessages;

        }
        /// <summary>
        /// User creates a new Chat Session.
        /// </summary>
        public async Task CreateNewChatSessionAsync(PartitionManager.RecordQueryParams rParams)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            string serialized_rId = serialize(rParams);
            var session = await SendRequest<Session>(HttpMethod.Post, $"/sessions/create/{serialized_rId}");

                _sessions.Add(session);
        }

        /// <summary>
        /// Rename the Chat Ssssion from "New Chat" to the summary provided by OpenAI
        /// </summary>
        public async Task RenameChatSessionAsync(PartitionManager.RecordQueryParams rParams, string newChatSessionName, bool onlyUpdateLocalSessionsCollection = false)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            var index = _sessions.FindIndex(s => s.SessionId == rParams.documentId);
            _sessions[index].Name = newChatSessionName;

            if (!onlyUpdateLocalSessionsCollection)
            {
                string serialized_rId = serialize(rParams);
                await SendRequest<Session>(HttpMethod.Post,
                    $"/sessions/rename/{serialized_rId}?newChatSessionName={newChatSessionName}");
            }
        }

        /// <summary>
        /// User deletes a chat session
        /// </summary>
        public async Task DeleteChatSessionAsync(PartitionManager.RecordQueryParams rParams)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            var index = _sessions.FindIndex(s => s.SessionId == rParams.documentId);
            _sessions.RemoveAt(index);
            string serialized_rId = serialize(rParams);
            await SendRequest(HttpMethod.Delete, $"/sessions/delete/{serialized_rId}");
        }

        /// <summary>
        /// Receive a prompt from a user, Vectorize it from _openAIService Get a completion from _openAiService
        /// </summary>
        public async Task<string> GetChatCompletionAsync(PartitionManager.RecordQueryParams rParams, string userPrompt, DateTime lastMessageTimestamp)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);

            string serialized_rId = serialize(rParams);
            var completion = await SendRequest<Completion>(HttpMethod.Post,
                $"/sessions/message/completion/{serialized_rId}", userPrompt);

            // Refresh the local messages cache:
            await GetChatSessionMessagesUpdatesAsync(rParams, lastMessageTimestamp);
            return completion.Text;
        }

        public async Task<CompletionPrompt> GetCompletionPrompt(PartitionManager.RecordQueryParams rParams)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(rParams.documentId);

            string serialized_rId = serialize(rParams);
            var completionPrompt = await SendRequest<CompletionPrompt>(HttpMethod.Get,
                $"/sessions/message/completionprompts/{serialized_rId}");
            return completionPrompt;
        }

        public async Task<string> SummarizeChatSessionNameAsync(PartitionManager.RecordQueryParams rParams, string prompt)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);
            string serialized_rId = serialize(rParams);
            var response = await SendRequest<Completion>(HttpMethod.Post,
                $"/sessions/summarize-name/{serialized_rId}", prompt);

            await RenameChatSessionAsync(rParams, response.Text, true);

            return response.Text;
        }

        /// <summary>
        /// Rate an assistant message. This can be used to discover useful AI responses for training, discoverability, and other benefits down the road.
        /// </summary>
        public async Task<Message> RateMessageAsync(PartitionManager.RecordQueryParams rParams, bool? rating)
        {
            ArgumentNullException.ThrowIfNull(rParams.documentId);
            string serialized_rId = serialize(rParams);
            string url = rating == null 
                        ? $"/sessions/message/rate/{serialized_rId}" 
                        : $"/sessions/message/rate/{serialized_rId}?rating={rating}";

            return await SendRequest<Message>(HttpMethod.Post, url);
        }

        private async Task<T> SendRequest<T>(HttpMethod method, string requestUri, object payload = null)
        {
            HttpResponseMessage responseMessage;
            switch (method)
            {
                case HttpMethod m when m == HttpMethod.Get:
                    responseMessage = await _httpClient.GetAsync($"{_settings.APIRoutePrefix}{requestUri}");
                    break;
                case HttpMethod m when m == HttpMethod.Post:
                    responseMessage = await _httpClient.PostAsync($"{_settings.APIRoutePrefix}{requestUri}",
                        payload == null ? null : JsonContent.Create(payload, payload.GetType()));
                    break;
                default:
                    throw new NotImplementedException($"The Http method {method.Method} is not supported.");
            }

            var content = await responseMessage.Content.ReadAsStringAsync();
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(content);
        }

        private async Task SendRequest(HttpMethod method, string requestUri)
        {
            switch (method)
            {
                case HttpMethod m when m == HttpMethod.Delete:
                    await _httpClient.DeleteAsync($"{_settings.APIRoutePrefix}{requestUri}");
                    break;
                default:
                    throw new NotImplementedException($"The Http method {method.Method} is not supported.");
            }
        }
    }

    class MessageEqualityComparer : IEqualityComparer<Message>
    {
        public bool Equals(Message x, Message y)
        {
            // Compare Messages based on their Id property
            return x.Id == y.Id;
        }

        public int GetHashCode(Message obj)
        {
            // Generate a hash code based on the Id property
            return obj.Id.GetHashCode();
        }
    }
}
