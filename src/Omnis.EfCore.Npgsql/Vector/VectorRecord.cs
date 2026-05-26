namespace Omnis.EfCore.Npgsql.Vector;

/// <summary>
/// 写入向量存储的分片向量记录。
/// </summary>
public sealed record VectorRecord(
    Guid ChunkId,
    string TenantId,
    string WorkspaceId,
    Guid KnowledgeBaseId,
    Guid DocumentId,
    string ContentHash,
    string EmbeddingId,
    string AclHash,
    double[] Vector,
    Guid? CreatedBy = null,
    DateTimeOffset? CreatedAt = null,
    Guid? UpdatedBy = null,
    DateTimeOffset? UpdatedAt = null,
    bool IsDeleted = false);
