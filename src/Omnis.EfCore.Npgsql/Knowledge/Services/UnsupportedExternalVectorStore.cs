using Omnis.EfCore.Npgsql.Vector;

namespace Omnis.EfCore.Npgsql.Knowledge.Services;

/// <summary>
/// 外部向量库占位实现，用于明确提示 Qdrant/Milvus 需要接入具体 SDK。
/// </summary>
internal sealed class UnsupportedExternalVectorStore(string provider) : IKnowledgeVectorStore
{
    /// <summary>外部向量库尚未配置时拒绝写入。</summary>
    public Task UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{provider} vector storage is not configured yet. The vector store abstraction is ready for this provider.");
    }

    /// <summary>外部向量库尚未配置时拒绝同步权限元数据。</summary>
    public Task UpdateAclHashAsync(Guid documentId, string aclHash, Guid? updatedBy = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException($"{provider} vector storage is not configured yet. The vector store abstraction is ready for this provider.");
    }
}
