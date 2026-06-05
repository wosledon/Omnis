using Omnis.Contracts.Llm;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Llm.Entities;

/// <summary>
/// LLM 模型配置实体，保存租户/工作空间/应用级路由范围、Provider 参数和凭据密文。
/// </summary>
public sealed class LlmModelConfigEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;
    /// <summary>应用标识；为空时作为工作空间默认模型参与路由。</summary>
    public string? ApplicationId { get; set; }
    /// <summary>配置名称，供后台展示和审计日志快照使用。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>模型服务提供方。</summary>
    public LlmProviderType Provider { get; set; }
    /// <summary>模型名或 Provider 要求的模型标识。</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Provider 基础地址。</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>Azure OpenAI 部署名；为空时回退使用 Model。</summary>
    public string? DeploymentName { get; set; }
    /// <summary>配置状态。</summary>
    public LlmModelStatus Status { get; set; } = LlmModelStatus.Active;
    /// <summary>路由优先级，数值越小越优先。</summary>
    public int Priority { get; set; } = 100;
    /// <summary>备用模型配置 ID，主模型失败时沿该链路降级。</summary>
    public Guid? FallbackModelConfigId { get; set; }
    /// <summary>单次 Provider 调用超时时间。</summary>
    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>连续失败达到该阈值后打开熔断。</summary>
    public int FailureThreshold { get; set; } = 3;
    /// <summary>熔断打开后的跳过窗口时长。</summary>
    public int CircuitBreakSeconds { get; set; } = 60;
    /// <summary>输入 Token 单价，单位为每 1K Token。</summary>
    public decimal? PromptTokenPricePer1K { get; set; }
    /// <summary>输出 Token 单价，单位为每 1K Token。</summary>
    public decimal? CompletionTokenPricePer1K { get; set; }
    /// <summary>Provider 运行参数 JSON，例如 temperature、top_p 或 apiVersion。</summary>
    public string ParametersJson { get; set; } = "{}";
    /// <summary>Provider 凭据 JSON，例如 apiKey 和 organization；不会透出到 DTO。</summary>
    public string CredentialsJson { get; set; } = "{}";
}
