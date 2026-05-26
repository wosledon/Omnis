using Omnis.Contracts.Llm;

namespace Omnis.Llm;

/// <summary>
/// LLM 网关应用服务边界，统一承载模型配置、路由、调用、流式输出和审计查询。
/// </summary>
public interface ILlmGateway
{
    /// <summary>创建模型配置。</summary>
    Task<LlmModelConfigDto> CreateModelConfigAsync(
        CreateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按租户、工作空间、应用和状态筛选模型配置。</summary>
    Task<IReadOnlyCollection<LlmModelConfigDto>> ListModelConfigsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        LlmModelStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>查询单个模型配置。</summary>
    Task<LlmModelConfigDto?> GetModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>更新模型配置。</summary>
    Task<LlmModelConfigDto?> UpdateModelConfigAsync(
        Guid modelConfigId,
        UpdateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>停用模型配置，使其不再参与路由。</summary>
    Task<LlmModelConfigDto?> DisableModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>执行一次非流式 LLM 调用，自动处理路由、审计、熔断和备用模型降级。</summary>
    Task<LlmCompletionResponse> CompleteAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>执行一次兼容流式 LLM 调用，返回可直接写入 SSE 的增量片段。</summary>
    IAsyncEnumerable<LlmStreamChunk> StreamAsync(
        LlmCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>查询 LLM 调用审计日志。</summary>
    Task<IReadOnlyCollection<LlmInvocationLogDto>> ListInvocationLogsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default);
}
