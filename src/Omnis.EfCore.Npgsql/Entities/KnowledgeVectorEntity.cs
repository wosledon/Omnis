using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 向量持久化实体；默认存放在 PostgreSQL，也可映射到 Qdrant/Milvus 的 payload。
/// </summary>
public sealed class KnowledgeVectorEntity : IAuditableEntity, ISoftDeleteEntity
{
    /// <summary>分片 ID，当前作为向量记录主键。</summary>
    public Guid ChunkId { get; set; }

    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>所属知识库 ID。</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>所属文档 ID。</summary>
    public Guid DocumentId { get; set; }

    /// <summary>内容哈希。</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Embedding 标识。</summary>
    public string EmbeddingId { get; set; } = string.Empty;

    /// <summary>权限快照哈希。</summary>
    public string AclHash { get; set; } = string.Empty;

    /// <summary>向量值；PG 默认使用 double precision[] 保存。</summary>
    public double[] Vector { get; set; } = [];

    /// <summary>创建人。</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>最后更新人。</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>最后更新时间。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>软删除标记。</summary>
    public bool IsDeleted { get; set; }
}
