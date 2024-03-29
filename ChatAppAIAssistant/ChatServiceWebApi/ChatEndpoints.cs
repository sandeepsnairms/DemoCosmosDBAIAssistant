using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Newtonsoft.Json;
using VectorSearchAiAssistant.Service.Interfaces;
using VectorSearchAiAssistant.Service.Models.Chat;
using VectorSearchAiAssistant.Service.Models.Search;
using VectorSearchAiAssistant.Service.Utils;
using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;
using User = VectorSearchAiAssistant.Service.Models.Search.User;

namespace ChatServiceWebApi
{
    public class ChatEndpoints
    {
        private readonly IChatService _chatService;

        public ChatEndpoints(IChatService chatService)
        {
            _chatService = chatService;
        }

        private PartitionManager.RecordQueryParams deserialize(string s)
        {
            return JsonConvert.DeserializeObject<PartitionManager.RecordQueryParams>(s);
        }

        public void Map(WebApplication app)
        {
            app.MapGet("/status", () => _chatService.Status)
                .WithName("GetServiceStatus");


            //message management 

            app.MapGet("/sessions/{rId_Serialized}/messages/{oldestMessageTimestamp}",
                    async(string rId_Serialized, string oldestMessageTimestamp) =>
                    await _chatService.GetChatSessionMessagesAsync(deserialize(rId_Serialized), new DateTime(long.Parse(oldestMessageTimestamp))))
                .WithName("GetChatSessionMessages");

            app.MapGet("/sessions/{rId_Serialized}/new_messages/{latestMessageTimestamp}",
                    async (string rId_Serialized, string latestMessageTimestamp) =>
                    await _chatService.GetChatSessionNewMessagesAsync(deserialize(rId_Serialized), new DateTime(long.Parse(latestMessageTimestamp))))
                .WithName("GetChatSessionNewMessages");

            app.MapPost("/sessions/message/rate/{rId_Serialized}", 
                    async (string rId_Serialized, bool? rating) =>
                    await _chatService.RateMessageAsync(deserialize(rId_Serialized), rating))
                .WithName("RateMessage");

            //meesage completion 

            app.MapPost("/sessions/message/completion/{rId_Serialized}", async (string rId_Serialized, [FromBody] string userPrompt) =>
                    await _chatService.GetChatCompletionAsync(deserialize(rId_Serialized), userPrompt))
                .WithName("GetChatCompletion");

            app.MapGet("/sessions/message/completionprompts/{rId_Serialized}",
                    async (string rId_Serialized) =>
                    await _chatService.GetCompletionPrompt(deserialize(rId_Serialized)))
                .WithName("GetCompletionPrompt");


            //session management 

            app.MapGet("/sessions/{rId_Serialized}", async (string rId_Serialized) => await _chatService.GetAllChatSessionsAsync(deserialize(rId_Serialized)))
                .WithName("GetAllChatSessions");

            app.MapPost("/sessions/create/{rId_Serialized}", async (string rId_Serialized) => await _chatService.CreateNewChatSessionAsync(deserialize(rId_Serialized)))
                .WithName("CreateNewChatSession");

            app.MapPost("/sessions/summarize-name/{rId_Serialized}", async (string rId_Serialized, [FromBody] string prompt) =>
                   await _chatService.SummarizeChatSessionNameAsync(deserialize(rId_Serialized), prompt))
               .WithName("SummarizeChatSessionName");

            app.MapPost("/sessions/rename/{rId_Serialized}", async (string rId_Serialized, string newChatSessionName) =>
                    await _chatService.RenameChatSessionAsync(deserialize(rId_Serialized), newChatSessionName))
                .WithName("RenameChatSession");

            app.MapDelete("/sessions/delete/{rId_Serialized}", async (string rId_Serialized) =>
                    await _chatService.DeleteChatSessionAsync(deserialize(rId_Serialized)))
                .WithName("DeleteChatSession");


            //search related 

            app.MapPost("/sessions/{rId_Serialized}/search", async (string rId_Serialized, string? searchString) =>
                    await _chatService.GetSessionsBySearchAsync(deserialize(rId_Serialized), searchString))
                .WithName("searchChat");

        }
    }
}
