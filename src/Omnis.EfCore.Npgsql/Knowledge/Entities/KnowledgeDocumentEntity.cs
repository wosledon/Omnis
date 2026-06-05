using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Knowledge.Entities;

/// <summary>
/// 知识文档持久化实体。
/// </summary>
public sealed class KnowledgeDocumentEntity : EntityBase
{
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid KnowledgeBaseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DocumentSourceType SourceType { get; set; } = DocumentSourceType.Upload;
    public string? FileUri { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public DocumentVisibility Visibility { get; set; } = DocumentVisibility.Internal;
    public string[] Tags { get; set; } = [];
    public string? DirectoryPath { get; set; }
    public int Version { get; set; } = 1;
    public int ChunkCount { get; set; }
    public string? FailureReason { get; set; }
}
