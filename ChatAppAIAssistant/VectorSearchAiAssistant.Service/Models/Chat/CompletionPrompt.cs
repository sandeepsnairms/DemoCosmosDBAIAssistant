using Azure.Search.Documents.Indexes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VectorSearchAiAssistant.Service.Models.Chat
{
    public class CompletionPrompt
    {
        public string Id { get; set; }
        public string Type { get; set; }
        [SimpleField]
        public string TenantId { get; set; }
        [SimpleField]
        public string UserId { get; set; }

        [SimpleField]
        public string SessionId { get; set; }

        /// <summary>
        /// Partition key
        /// </summary>
        [SimpleField]
        public string pk { get; set; }
        public string MessageId { get; set; }
        public string Prompt { get; set; }

        public CompletionPrompt(string tenantId, string userId, string sessionId, string messageId, string prompt)
        {
            Id = Guid.NewGuid().ToString();
            Type = nameof(CompletionPrompt);
            SessionId = sessionId;
            MessageId = messageId;
            Prompt = prompt;
            TenantId = tenantId;
            UserId = userId;
            pk = sessionId;
        }
    }
}
