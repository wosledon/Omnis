using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Knowledge.Entities;

/// <summary>
/// 知识模块审计日志持久化实体。
/// </summary>
public sealed class KnowledgeAuditLogEntity : EntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? ActorId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
