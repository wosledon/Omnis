using Omnis.Contracts.Llm;

namespace Omnis.Llm;

/// <summary>
/// 运行时使用的熔断快照，避免网关层直接依赖 EF 实体。
/// </summary>
public sealed record LlmCircuitSnapshot(
    LlmCircuitState State,
    int FailureCount,
    DateTimeOffset? OpenedUntil);

/// <summary>
/// Provider 调用所需的完整模型配置记录，包含仅后端可见的凭据。
/// </summary>
public sealed record LlmModelConfigRecord(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string? ApplicationId,
    string Name,
    LlmProviderType Provider,
    string Model,
    string Endpoint,
    string? DeploymentName,
    LlmModelStatus Status,
    int Priority,
    Guid? FallbackModelConfigId,
    int TimeoutSeconds,
    int FailureThreshold,
    int CircuitBreakSeconds,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyDictionary<string, string> Credentials);

/// <summary>
/// 写入持久化层的 LLM 调用审计记录。
/// </summary>
public sealed record LlmInvocationLogRecord(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string? ApplicationId,
    Guid ModelConfigId,
    string ModelConfigName,
    LlmProviderType Provider,
    string Model,
    string RequestJson,
    string ResponseJson,
    LlmInvocationStatus Status,
    bool UsedFallback,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    long DurationMs,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

/// <summary>
/// 传递给具体 Provider Client 的规范化调用请求。
/// </summary>
internal sealed record LlmProviderRequest(
    LlmModelConfigRecord Config,
    IReadOnlyList<LlmChatMessage> Messages,
    double? Temperature,
    int? MaxTokens,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Provider Client 返回的原始调用结果，网关会再补充路由和审计信息。
/// </summary>
internal sealed record LlmProviderResult(
    string Content,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string RawJson);

/// <summary>
/// 模型 Provider 客户端抽象，负责把统一请求转换为 OpenAI/Azure/OpenAI-compatible HTTP 调用。
/// </summary>
internal interface ILlmProviderClient
{
    /// <summary>执行非流式模型调用。</summary>
    Task<LlmProviderResult> CompleteAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>执行原生流式模型调用，逐步返回文本增量。</summary>
    IAsyncEnumerable<string> StreamAsync(
        LlmProviderRequest request,
        CancellationToken cancellationToken = default);
}
