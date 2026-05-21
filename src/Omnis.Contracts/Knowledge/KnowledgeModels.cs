namespace Omnis.Contracts.Knowledge;

/// <summary>
/// 知识库对外返回模型，表达租户、工作空间和默认权限策略。
/// </summary>
public sealed record KnowledgeBaseDto(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string Name,
    string? Description,
    KnowledgeBaseVisibility DefaultVisibility,
    DateTimeOffset CreatedAt);

/// <summary>
/// 文档 ACL 条目返回模型。
/// </summary>
public sealed record DocumentAclEntryDto(
    Guid Id,
    AclPrincipalType PrincipalType,
    string PrincipalId,
    DocumentPermission Permission);

/// <summary>
/// 文档返回模型，包含处理状态、分类标签和分片数量。
/// </summary>
public sealed record DocumentDto(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    Guid KnowledgeBaseId,
    string Name,
    DocumentSourceType SourceType,
    string? FileUri,
    DocumentStatus Status,
    DocumentVisibility Visibility,
    string[] Tags,
    string? DirectoryPath,
    int Version,
    int ChunkCount,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 文档分片返回模型，是知识检索和引用溯源的最小业务单元。
/// </summary>
public sealed record DocumentChunkDto(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    Guid KnowledgeBaseId,
    Guid DocumentId,
    int Index,
    string Content,
    string ContentHash,
    string EmbeddingId,
    string AclHash,
    DateTimeOffset CreatedAt);

/// <summary>
/// 文档处理状态查询结果。
/// </summary>
public sealed record DocumentProcessingStatusDto(
    Guid DocumentId,
    DocumentStatus Status,
    int ChunkCount,
    string? FailureReason,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 创建知识库请求。
/// </summary>
public sealed record CreateKnowledgeBaseRequest(
    string TenantId,
    string WorkspaceId,
    string Name,
    string? Description,
    KnowledgeBaseVisibility DefaultVisibility = KnowledgeBaseVisibility.Members);

/// <summary>
/// 更新文档可见性和 ACL 的请求。
/// </summary>
public sealed record UpdateDocumentAclRequest(
    DocumentVisibility Visibility,
    IReadOnlyCollection<UpsertDocumentAclEntry> Acl);

/// <summary>
/// 创建或替换 ACL 时使用的授权条目。
/// </summary>
public sealed record UpsertDocumentAclEntry(
    AclPrincipalType PrincipalType,
    string PrincipalId,
    DocumentPermission Permission);

/// <summary>
/// 上传文档时附带的租户、分类和权限选项。
/// </summary>
public sealed record UploadDocumentOptions(
    string TenantId,
    string WorkspaceId,
    DocumentVisibility Visibility,
    string[] Tags,
    string? DirectoryPath,
    IReadOnlyCollection<UpsertDocumentAclEntry> Acl);

/// <summary>
/// 知识模块审计日志返回模型，用于追踪上传、处理和权限变更。
/// </summary>
public sealed record AuditLogDto(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string Action,
    string EntityType,
    Guid EntityId,
    string? ActorId,
    string? Before,
    string? After,
    DateTimeOffset CreatedAt);
