using Omnis.Contracts.Chat;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Chat.Entities;

/// <summary>
/// 会话持久化实体，对应 conversations 表。
/// </summary>
public sealed class ConversationEntity : EntityBase
{
    /// <summary>租户标识，所有会话查询都必须携带该隔离边界。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识，用于业务线或部门级隔离。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>应用标识，可用于后续模型配置和 RAG 参数绑定。</summary>
    public string? ApplicationId { get; set; }

    /// <summary>终端用户标识。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>终端用户展示名。</summary>
    public string? UserName { get; set; }

    /// <summary>创建会话时的用户组快照，用于后续 RAG ACL 过滤。</summary>
    public string[] UserGroups { get; set; } = [];

    /// <summary>创建会话时的用户角色快照，用于后续 RAG ACL 过滤。</summary>
    public string[] UserRoles { get; set; } = [];

    /// <summary>会话来源渠道。</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>会话当前状态。</summary>
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    /// <summary>会话默认可检索知识库范围。</summary>
    public Guid[] KnowledgeBaseIds { get; set; } = [];

    /// <summary>会话关闭时间。</summary>
    public DateTime? ClosedAt { get; set; }
}
