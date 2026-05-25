using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Knowledge.Entities;

/// <summary>
/// 文档分片持久化实体。
/// </summary>
public sealed class DocumentChunkEntity : EntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid KnowledgeBaseId { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string EmbeddingId { get; set; } = string.Empty;
    public string AclHash { get; set; } = string.Empty;
}
