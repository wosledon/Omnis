namespace Omnis.Contracts.Llm;

/// <summary>
/// 模型配置当前熔断快照，供管理后台展示模型健康状态。
/// </summary>
public sealed record LlmCircuitStateDto(
    LlmCircuitState State,
    int FailureCount,
    DateTimeOffset? OpenedUntil,
    DateTimeOffset? LastFailureAt,
    DateTimeOffset? LastSuccessAt);

/// <summary>
/// LLM 模型配置返回模型。敏感凭据不会出现在该 DTO 中。
/// </summary>
public sealed record LlmModelConfigDto(
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
    decimal? PromptTokenPricePer1K,
    decimal? CompletionTokenPricePer1K,
    IReadOnlyDictionary<string, string> Parameters,
    bool CredentialsConfigured,
    LlmCircuitStateDto Circuit,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 创建 LLM 模型配置请求，包含路由范围、Provider、模型参数、熔断参数和凭据。
/// </summary>
public sealed record CreateLlmModelConfigRequest(
    string TenantId,
    string WorkspaceId,
    string? ApplicationId,
    string Name,
    LlmProviderType Provider,
    string Model,
    string Endpoint,
    string? DeploymentName = null,
    LlmModelStatus Status = LlmModelStatus.Active,
    int Priority = 100,
    Guid? FallbackModelConfigId = null,
    int TimeoutSeconds = 60,
    int FailureThreshold = 3,
    int CircuitBreakSeconds = 60,
    decimal? PromptTokenPricePer1K = null,
    decimal? CompletionTokenPricePer1K = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, string>? Credentials = null);

/// <summary>
/// 更新 LLM 模型配置请求。Credentials 为 null 时保留原凭据，非 null 时整体替换。
/// </summary>
public sealed record UpdateLlmModelConfigRequest(
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
    decimal? PromptTokenPricePer1K = null,
    decimal? CompletionTokenPricePer1K = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, string>? Credentials = null);

/// <summary>
/// Chat Completion 消息项，保持与主流 LLM 对话接口的 role/content 结构对齐。
/// </summary>
public sealed record LlmChatMessage(
    LlmMessageRole Role,
    string Content,
    string? Name = null);

/// <summary>
/// LLM 补全请求。未指定 ModelConfigId 时由网关按租户、工作空间和应用自动路由。
/// </summary>
public sealed record LlmCompletionRequest(
    string TenantId,
    string WorkspaceId,
    string? ApplicationId,
    IReadOnlyList<LlmChatMessage> Messages,
    Guid? ModelConfigId = null,
    double? Temperature = null,
    int? MaxTokens = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// LLM 非流式调用响应，同时返回模型路由结果、Token 统计和审计调用 ID。
/// </summary>
public sealed record LlmCompletionResponse(
    Guid InvocationId,
    Guid ModelConfigId,
    string ModelConfigName,
    LlmProviderType Provider,
    string Model,
    string Content,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    long DurationMs,
    LlmInvocationStatus Status,
    bool UsedFallback,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);

/// <summary>
/// LLM 流式响应片段，用于 SSE/WebSocket 逐步推送 token delta。
/// </summary>
public sealed record LlmStreamChunk(
    Guid InvocationId,
    Guid ModelConfigId,
    string ContentDelta,
    bool IsCompleted,
    string? FinishReason = null);

/// <summary>
/// LLM 调用审计日志返回模型，用于后台排障、成本统计和合规审查。
/// </summary>
public sealed record LlmInvocationLogDto(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string? ApplicationId,
    Guid ModelConfigId,
    string ModelConfigName,
    LlmProviderType Provider,
    string Model,
    LlmInvocationStatus Status,
    bool UsedFallback,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    long DurationMs,
    string? ErrorMessage,
    DateTimeOffset CreatedAt);
