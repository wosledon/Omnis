namespace Omnis.EfCore.Npgsql.Rag.Entities;

/// <summary>
/// RAG 推理观测日志实体，对应 rag_inference_logs 表。
/// </summary>
public sealed class RagInferenceLogEntity
{
    /// <summary>日志主键。</summary>
    public Guid Id { get; set; }
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;
    /// <summary>应用标识。</summary>
    public string? ApplicationId { get; set; }
    /// <summary>会话标识。</summary>
    public string? ConversationId { get; set; }
    /// <summary>消息标识。</summary>
    public string? MessageId { get; set; }
    /// <summary>提问用户标识。</summary>
    public string UserId { get; set; } = string.Empty;
    /// <summary>用户原始问题。</summary>
    public string UserQuestion { get; set; } = string.Empty;
    /// <summary>改写后的检索 query。</summary>
    public string RewrittenQuery { get; set; } = string.Empty;
    /// <summary>检索到的片段 JSON。</summary>
    public string RetrievedChunksJson { get; set; } = "[]";
    /// <summary>最终发送给模型的 Prompt。</summary>
    public string FinalPrompt { get; set; } = string.Empty;
    /// <summary>模型原始输出。</summary>
    public string LlmRawOutput { get; set; } = string.Empty;
    /// <summary>最终回答。</summary>
    public string FinalAnswer { get; set; } = string.Empty;
    /// <summary>置信度分数。</summary>
    public decimal ConfidenceScore { get; set; }
    /// <summary>引用来源 ID 数组。</summary>
    public string[] CitationSourceIds { get; set; } = [];
    /// <summary>是否检测到幻觉或引用异常。</summary>
    public bool HasHallucination { get; set; }
    /// <summary>检索耗时，单位毫秒。</summary>
    public int RetrievalDurationMs { get; set; }
    /// <summary>生成耗时，单位毫秒。</summary>
    public int GenerationDurationMs { get; set; }
    /// <summary>整段推理总耗时，单位毫秒。</summary>
    public int InferenceDurationMs { get; set; }
    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; }
}
