using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Entities;

/// <summary>
/// 知识文档持久化实体。
/// </summary>
public sealed class KnowledgeDocumentEntity : EntityBase
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>所属知识库 ID。</summary>
    public Guid KnowledgeBaseId { get; set; }

    /// <summary>文档名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文档来源类型。</summary>
    public DocumentSourceType SourceType { get; set; } = DocumentSourceType.Upload;

    /// <summary>文件存储地址或来源地址。</summary>
    public string? FileUri { get; set; }

    /// <summary>文档处理状态。</summary>
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;

    /// <summary>文档可见性策略。</summary>
    public DocumentVisibility Visibility { get; set; } = DocumentVisibility.Internal;

    /// <summary>文档标签。</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>目录路径，用于知识分类。</summary>
    public string? DirectoryPath { get; set; }

    /// <summary>文档版本号。</summary>
    public int Version { get; set; } = 1;

    /// <summary>分片数量。</summary>
    public int ChunkCount { get; set; }

    /// <summary>处理失败原因。</summary>
    public string? FailureReason { get; set; }
}
