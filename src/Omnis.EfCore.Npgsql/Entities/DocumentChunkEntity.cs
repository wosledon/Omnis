using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 文档分片持久化实体。
/// </summary>
public sealed class DocumentChunkEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>所属知识库 ID。</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>所属文档 ID。</summary>
    public Guid DocumentId { get; set; }

    /// <summary>文档内分片序号。</summary>
    public int ChunkIndex { get; set; }

    /// <summary>分片文本内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>内容哈希，用于去重和追踪。</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Embedding 标识。</summary>
    public string EmbeddingId { get; set; } = string.Empty;

    /// <summary>权限快照哈希，检索阶段用于下推过滤。</summary>
    public string AclHash { get; set; } = string.Empty;
}
