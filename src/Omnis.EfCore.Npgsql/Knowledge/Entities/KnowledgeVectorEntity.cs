using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Knowledge.Entities;

/// <summary>
/// 知识向量持久化实体。
/// </summary>
public sealed class KnowledgeVectorEntity : IAuditableEntity, ISoftDeleteEntity
{
    public Guid ChunkId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid KnowledgeBaseId { get; set; }
    public Guid DocumentId { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string EmbeddingId { get; set; } = string.Empty;
    public string AclHash { get; set; } = string.Empty;
    public double[] Vector { get; set; } = [];
    public Guid? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
