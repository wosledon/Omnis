using Omnis.Contracts.Llm;

namespace Omnis.EfCore.Npgsql.Llm.Entities;

/// <summary>
/// LLM 调用审计日志实体，记录模型、耗时、Token、状态和原始请求/响应快照。
/// </summary>
public sealed class LlmInvocationLogEntity
{
    /// <summary>审计日志主键，同时作为调用 ID 返回给 API 调用方。</summary>
    public Guid Id { get; set; }
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;
    /// <summary>应用标识。</summary>
    public string? ApplicationId { get; set; }
    /// <summary>实际调用的模型配置 ID。</summary>
    public Guid ModelConfigId { get; set; }
    /// <summary>调用发生时的模型配置名称快照。</summary>
    public string ModelConfigName { get; set; } = string.Empty;
    /// <summary>调用发生时的 Provider 快照。</summary>
    public LlmProviderType Provider { get; set; }
    /// <summary>调用发生时的模型名快照。</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>请求快照 JSON，便于排障和合规审查。</summary>
    public string RequestJson { get; set; } = "{}";
    /// <summary>Provider 原始响应 JSON。</summary>
    public string ResponseJson { get; set; } = "{}";
    /// <summary>调用状态。</summary>
    public LlmInvocationStatus Status { get; set; }
    /// <summary>是否使用了备用模型。</summary>
    public bool UsedFallback { get; set; }
    /// <summary>输入 Token 数。</summary>
    public int PromptTokens { get; set; }
    /// <summary>输出 Token 数。</summary>
    public int CompletionTokens { get; set; }
    /// <summary>总 Token 数。</summary>
    public int TotalTokens { get; set; }
    /// <summary>调用耗时，单位毫秒。</summary>
    public long DurationMs { get; set; }
    /// <summary>失败原因。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>日志创建时间。</summary>
    public DateTime CreatedAt { get; set; }
}
