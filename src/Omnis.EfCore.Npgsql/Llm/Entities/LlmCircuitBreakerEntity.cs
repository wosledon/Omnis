using Omnis.Contracts.Llm;

namespace Omnis.EfCore.Npgsql.Llm.Entities;

/// <summary>
/// LLM 模型熔断状态实体，按模型配置维度记录连续失败和打开窗口。
/// </summary>
public sealed class LlmCircuitBreakerEntity
{
    /// <summary>模型配置 ID，同时作为熔断表主键。</summary>
    public Guid ModelConfigId { get; set; }
    /// <summary>当前熔断状态。</summary>
    public LlmCircuitState State { get; set; }
    /// <summary>连续失败次数。</summary>
    public int FailureCount { get; set; }
    /// <summary>熔断打开截止时间。</summary>
    public DateTime? OpenedUntil { get; set; }
    /// <summary>最近一次失败时间。</summary>
    public DateTime? LastFailureAt { get; set; }
    /// <summary>最近一次成功时间。</summary>
    public DateTime? LastSuccessAt { get; set; }
    /// <summary>熔断状态更新时间。</summary>
    public DateTime UpdatedAt { get; set; }
}
