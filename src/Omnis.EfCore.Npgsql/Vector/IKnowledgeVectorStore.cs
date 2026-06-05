namespace Omnis.EfCore.Npgsql.Vector;

/// <summary>
/// 知识向量存储抽象；默认实现是 PostgreSQL，后续可接 Qdrant/Milvus。
/// </summary>
public interface IKnowledgeVectorStore
{
    /// <summary>批量新增或更新分片向量。</summary>
    Task UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default);

    /// <summary>文档 ACL 变化后同步向量元数据中的权限哈希。</summary>
    Task UpdateAclHashAsync(Guid documentId, string aclHash, Guid? updatedBy = null, CancellationToken cancellationToken = default);
}
