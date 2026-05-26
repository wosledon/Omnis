using Omnis.Contracts.Llm;

namespace Omnis.Llm;

/// <summary>
/// LLM 网关持久化抽象，由基础设施层实现模型配置、审计日志和熔断状态存取。
/// </summary>
public interface ILlmGatewayStore
{
    /// <summary>创建模型配置并初始化熔断状态。</summary>
    Task<LlmModelConfigDto> CreateModelConfigAsync(
        CreateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>查询模型配置列表。</summary>
    Task<IReadOnlyCollection<LlmModelConfigDto>> ListModelConfigsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        LlmModelStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>查询模型配置 DTO。</summary>
    Task<LlmModelConfigDto?> GetModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>查询运行时模型配置，包含调用 Provider 所需的凭据。</summary>
    Task<LlmModelConfigRecord?> GetModelConfigRecordAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>更新模型配置。</summary>
    Task<LlmModelConfigDto?> UpdateModelConfigAsync(
        Guid modelConfigId,
        UpdateLlmModelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>停用模型配置。</summary>
    Task<LlmModelConfigDto?> DisableModelConfigAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>按请求范围加载可参与路由的模型候选列表。</summary>
    Task<IReadOnlyList<LlmModelConfigRecord>> ListRouteCandidatesAsync(
        string tenantId,
        string workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>读取模型当前熔断状态。</summary>
    Task<LlmCircuitSnapshot> GetCircuitAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>记录模型调用成功，并关闭熔断。</summary>
    Task RecordCircuitSuccessAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default);

    /// <summary>记录模型调用失败，达到阈值后打开熔断窗口。</summary>
    Task RecordCircuitFailureAsync(
        Guid modelConfigId,
        int failureThreshold,
        int circuitBreakSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>保存一次 LLM 调用审计日志。</summary>
    Task<LlmInvocationLogDto> SaveInvocationLogAsync(
        LlmInvocationLogRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>查询 LLM 调用审计日志。</summary>
    Task<IReadOnlyCollection<LlmInvocationLogDto>> ListInvocationLogsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        Guid? modelConfigId,
        CancellationToken cancellationToken = default);
}
