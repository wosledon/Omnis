namespace Omnis.EfCore.Npgsql.Rag.Entities;

public sealed class RagInferenceLogEntity
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? ApplicationId { get; set; }
    public string? ConversationId { get; set; }
    public string? MessageId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string RewrittenQuery { get; set; } = string.Empty;
    public string RetrievedChunksJson { get; set; } = "[]";
    public string FinalPrompt { get; set; } = string.Empty;
    public string LlmRawOutput { get; set; } = string.Empty;
    public string FinalAnswer { get; set; } = string.Empty;
    public decimal ConfidenceScore { get; set; }
    public string[] CitationSourceIds { get; set; } = [];
    public bool HasHallucination { get; set; }
    public int RetrievalDurationMs { get; set; }
    public int GenerationDurationMs { get; set; }
    public int InferenceDurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
