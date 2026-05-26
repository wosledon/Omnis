using Omnis.Contracts.Chat;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Chat.Entities;

/// <summary>
/// 人工转接持久化实体，对应 human_handoffs 表。
/// </summary>
public sealed class HumanHandoffEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>所属会话 ID。</summary>
    public Guid ConversationId { get; set; }

    /// <summary>触发转人工的原因。</summary>
    public HandoffTriggerType TriggerType { get; set; }

    /// <summary>转人工摘要 JSON，供坐席工作台快速展示。</summary>
    public string SummaryJson { get; set; } = "{}";

    /// <summary>触发转接时关联的上一条 AI 消息。</summary>
    public Guid? LastAiMessageId { get; set; }

    /// <summary>人工转接状态。</summary>
    public HandoffStatus Status { get; set; } = HandoffStatus.Queued;

    /// <summary>被分配的坐席 ID。</summary>
    public string? AssignedAgentId { get; set; }
}
