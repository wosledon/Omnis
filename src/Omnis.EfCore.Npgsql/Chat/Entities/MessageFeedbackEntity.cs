using Omnis.Contracts.Chat;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Chat.Entities;

/// <summary>
/// 消息反馈持久化实体，对应 message_feedback 表。
/// </summary>
public sealed class MessageFeedbackEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>被评价的消息 ID。</summary>
    public Guid MessageId { get; set; }

    /// <summary>所属会话 ID，冗余保存便于后台筛选。</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>反馈用户 ID。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>点赞或点踩。</summary>
    public MessageFeedbackRating Rating { get; set; }

    /// <summary>反馈原因。</summary>
    public string? Reason { get; set; }

    /// <summary>被评价 AI 消息对应的 RAG 观测日志 ID。</summary>
    public Guid? RagInferenceLogId { get; set; }
}
