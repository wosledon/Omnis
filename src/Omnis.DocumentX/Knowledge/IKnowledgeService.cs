using Omnis.Contracts.Knowledge;

namespace Omnis.DocumentX.Knowledge;

/// <summary>
/// 知识管理模块应用服务接口，屏蔽内存、PostgreSQL 或未来外部服务实现差异。
/// </summary>
public interface IKnowledgeService
{
    /// <summary>创建知识库。</summary>
    Task<KnowledgeBaseDto> CreateKnowledgeBaseAsync(CreateKnowledgeBaseRequest request, CancellationToken cancellationToken = default);

    /// <summary>按租户和可选工作空间查询知识库列表。</summary>
    Task<IReadOnlyCollection<KnowledgeBaseDto>> ListKnowledgeBasesAsync(
        string tenantId,
        string? workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>按 ID 查询知识库详情。</summary>
    Task<KnowledgeBaseDto?> GetKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>上传并处理文档，包括解析、清洗、分片、向量化和权限元数据写入。</summary>
    Task<DocumentDto> UploadDocumentAsync(
        Guid knowledgeBaseId,
        string fileName,
        string contentType,
        Stream content,
        UploadDocumentOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>查询知识库下文档列表，支持标签和目录过滤。</summary>
    Task<IReadOnlyCollection<DocumentDto>> ListDocumentsAsync(
        Guid knowledgeBaseId,
        string tenantId,
        string workspaceId,
        string? tag,
        string? directoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>按 ID 查询文档详情。</summary>
    Task<DocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>查询文档处理状态，供前端轮询和失败重试入口使用。</summary>
    Task<DocumentProcessingStatusDto?> GetProcessingStatusAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>查询文档分片，用于管理后台预览和引用定位。</summary>
    Task<IReadOnlyCollection<DocumentChunkDto>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>查询文档 ACL。</summary>
    Task<IReadOnlyCollection<DocumentAclEntryDto>> GetAclAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>替换文档 ACL，并同步影响检索过滤元数据。</summary>
    Task<DocumentDto?> UpdateAclAsync(
        Guid documentId,
        UpdateDocumentAclRequest request,
        string? actorId,
        CancellationToken cancellationToken = default);

    /// <summary>查询知识模块审计日志。</summary>
    Task<IReadOnlyCollection<AuditLogDto>> GetAuditLogsAsync(
        string tenantId,
        Guid? entityId,
        CancellationToken cancellationToken = default);
}
