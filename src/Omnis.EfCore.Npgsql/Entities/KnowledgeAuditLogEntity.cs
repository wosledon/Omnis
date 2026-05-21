using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 知识模块审计日志实体。
/// </summary>
public sealed class KnowledgeAuditLogEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>操作名称。</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>实体类型。</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>实体 ID。</summary>
    public Guid EntityId { get; set; }

    /// <summary>操作人标识，保留外部身份字符串。</summary>
    public string? ActorId { get; set; }

    /// <summary>变更前 JSON 快照。</summary>
    public string? BeforeJson { get; set; }

    /// <summary>变更后 JSON 快照。</summary>
    public string? AfterJson { get; set; }
}
