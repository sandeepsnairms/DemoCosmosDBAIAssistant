using Azure.Search.Documents.Indexes;

namespace VectorSearchAiAssistant.Service.Models.Chat;

public record Message
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    [SearchableField(IsKey = true, IsFilterable = true)]
    public string Id { get; set; }
    [SimpleField]
    public string Type { get; set; }

    [SimpleField]
    public string SessionId { get; set; }

    /// <summary>
    /// Partition key
    /// </summary>
    [SimpleField]
    public string pk { get; set; }

    [SimpleField]
    public string TenantId { get; set; }
    [SimpleField]
    public string UserId { get; set; }
    [SimpleField]
    public DateTime TimeStamp { get; set; }
    [SimpleField]
    public string Sender { get; set; }
    [SimpleField]
    public int? Tokens { get; set; }
    [SimpleField]
    public string Text { get; set; }
    [SimpleField]
    public bool? Rating { get; set; }
    [FieldBuilderIgnore]
    public float[]? Vector { get; set; }
    public string CompletionPromptId { get; set; }

    public Message(string tenantId, string userId, string sessionId, string sender, int? tokens, string text, float[]? vector, bool? rating)
    {
        Id = Guid.NewGuid().ToString();
        Type = nameof(Message);
        TenantId = tenantId;
        UserId = userId;
        SessionId = sessionId;
        Sender = sender;
        Tokens = tokens ?? 0;
        TimeStamp = DateTime.UtcNow;
        Text = text;
        Rating = rating;
        Vector = vector;
        pk = sessionId;
    }
}