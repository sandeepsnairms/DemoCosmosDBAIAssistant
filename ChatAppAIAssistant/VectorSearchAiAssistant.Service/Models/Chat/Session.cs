using Azure.Search.Documents.Indexes;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Newtonsoft.Json;

namespace VectorSearchAiAssistant.Service.Models.Chat;

public record Session
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; }

    public string Type { get; set; }

    [SimpleField]
    public string SessionId { get; set; }

    /// <summary>
    /// Partition key
    /// </summary>
    [SimpleField]
    public string pk { get; set; }

    [SimpleField]
    public DateTime TimeStamp { get; set; }

    [SimpleField]
    public string TenantId { get; set; }

    [SimpleField]
    public string UserId { get; set; }

    public int? TokensUsed { get; set; }

    public string Name { get; set; }

    [JsonIgnore]
    public List<Message> Messages { get; set; }

    public Session(string tenantId, string userId)
    {
        Id = Guid.NewGuid().ToString();
        Type = nameof(Session);
        SessionId = Id;
        pk = Id;
        TenantId = tenantId;
        UserId = userId;
        TokensUsed = 0;
        Name = "New Chat";
        TimeStamp = DateTime.UtcNow;
        Messages = new List<Message>();
    }

    public void AddMessage(Message message)
    {
        Messages.Add(message);
    }

    public void UpdateMessage(Message message)
    {
        var match = Messages.Single(m => m.Id == message.Id);
        var index = Messages.IndexOf(match);
        Messages[index] = message;
    }
}