using Microsoft.EntityFrameworkCore;
using Omnis.EfCore.Npgsql.Knowledge.Entities;
using Omnis.EfCore.Npgsql.Vector;

namespace Omnis.EfCore.Npgsql.Knowledge.Services;

/// <summary>
/// PostgreSQL 向量存储实现，默认使用实体表保存 double precision[] 向量。
/// </summary>
internal sealed class PostgreSqlKnowledgeVectorStore(
    OmnisNpgsqlDbContext dbContext
) : IKnowledgeVectorStore
{
    /// <summary>
    /// 使用 ChunkId 做幂等更新，避免文档重试处理时产生重复向量。
    /// </summary>
    public async Task UpsertAsync(IReadOnlyCollection<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        var chunkIds = records.Select(record => record.ChunkId).ToArray();
        var existingVectors = await dbContext.KnowledgeVectors
            .IgnoreQueryFilters()
            .Where(vector => chunkIds.Contains(vector.ChunkId))
            .ToDictionaryAsync(vector => vector.ChunkId, cancellationToken);

        foreach (var record in records)
        {
            if (existingVectors.TryGetValue(record.ChunkId, out var entity))
            {
                UpdateVectorEntity(entity, record);
            }
            else
            {
                dbContext.KnowledgeVectors.Add(CreateVectorEntity(record));
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 文档权限变更后，同步刷新向量检索元数据中的 ACL 哈希。
    /// </summary>
    public async Task UpdateAclHashAsync(Guid documentId, string aclHash, Guid? updatedBy = null, CancellationToken cancellationToken = default)
    {
        var vectors = await dbContext.KnowledgeVectors
            .Where(vector => vector.DocumentId == documentId)
            .ToArrayAsync(cancellationToken);

        foreach (var vector in vectors)
        {
            vector.AclHash = aclHash;
            vector.UpdatedBy = updatedBy;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 根据向量记录创建新的持久化实体。
    /// </summary>
    static KnowledgeVectorEntity CreateVectorEntity(VectorRecord record)
    {
        var now = DateTime.UtcNow;
        return new KnowledgeVectorEntity
        {
            ChunkId = record.ChunkId,
            TenantId = record.TenantId,
            WorkspaceId = record.WorkspaceId,
            KnowledgeBaseId = record.KnowledgeBaseId,
            DocumentId = record.DocumentId,
            ContentHash = record.ContentHash,
            EmbeddingId = record.EmbeddingId,
            AclHash = record.AclHash,
            Vector = record.Vector,
            CreatedBy = record.CreatedBy,
            CreatedAt = ToUtcDateTime(record.CreatedAt) ?? now,
            UpdatedBy = record.UpdatedBy,
            UpdatedAt = ToUtcDateTime(record.UpdatedAt) ?? now,
            IsDeleted = record.IsDeleted
        };
    }

    /// <summary>
    /// 将传入记录覆盖到已有向量实体上。
    /// </summary>
    static void UpdateVectorEntity(KnowledgeVectorEntity entity, VectorRecord record)
    {
        entity.TenantId = record.TenantId;
        entity.WorkspaceId = record.WorkspaceId;
        entity.KnowledgeBaseId = record.KnowledgeBaseId;
        entity.DocumentId = record.DocumentId;
        entity.ContentHash = record.ContentHash;
        entity.EmbeddingId = record.EmbeddingId;
        entity.AclHash = record.AclHash;
        entity.Vector = record.Vector;
        entity.UpdatedBy = record.UpdatedBy;
        entity.UpdatedAt = ToUtcDateTime(record.UpdatedAt) ?? DateTime.UtcNow;
        entity.IsDeleted = record.IsDeleted;
    }

    /// <summary>
    /// 统一把 DTO 的 DateTimeOffset 转成实体使用的 UTC DateTime。
    /// </summary>
    static DateTime? ToUtcDateTime(DateTimeOffset? value)
    {
        return value?.UtcDateTime;
    }
}
