using Omnis.Contracts.Chat;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Chat.Entities;

/// <summary>
/// 会话消息持久化实体，对应 conversation_messages 表。
/// </summary>
public sealed class ConversationMessageEntity : EntityBase
{
    /// <summary>租户标识，用于消息历史和反馈查询的安全边界。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>所属会话 ID。</summary>
    public Guid ConversationId { get; set; }

    /// <summary>消息角色。</summary>
    public MessageRole Role { get; set; }

    /// <summary>消息正文。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>引用来源 JSON，仅 AI 消息通常有值。</summary>
    public string CitationsJson { get; set; } = "[]";

    /// <summary>AI 回复置信度，用户消息为空。</summary>
    public double? ConfidenceScore { get; set; }

    /// <summary>关联 RAG 观测日志 ID，便于管理后台展开调试链路。</summary>
    public Guid? RagInferenceLogId { get; set; }
}
