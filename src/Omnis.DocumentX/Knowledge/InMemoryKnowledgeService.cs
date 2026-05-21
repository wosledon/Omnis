using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Omnis.Contracts.Knowledge;

namespace Omnis.DocumentX.Knowledge;

/// <summary>
/// 内存版知识管理服务，实现与 PostgreSQL 版本相同的接口，便于测试和原型验证。
/// </summary>
internal sealed class InMemoryKnowledgeService(
    IDocumentTextExtractor extractor,
    ITextChunker chunker,
    IEmbeddingGenerator embeddingGenerator
) : IKnowledgeService
{
    // 以下集合模拟数据库表；生产默认使用 PostgreSQL 实现。
    readonly ConcurrentDictionary<Guid, KnowledgeBaseRecord> knowledgeBases = new();
    readonly ConcurrentDictionary<Guid, DocumentRecord> documents = new();
    readonly ConcurrentDictionary<Guid, List<DocumentChunkRecord>> chunksByDocument = new();
    readonly ConcurrentDictionary<Guid, List<DocumentAclEntryRecord>> aclByDocument = new();
    readonly ConcurrentQueue<AuditLogRecord> auditLogs = new();

    /// <summary>
    /// 创建知识库并记录审计日志。
    /// </summary>
    public Task<KnowledgeBaseDto> CreateKnowledgeBaseAsync(CreateKnowledgeBaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var now = DateTimeOffset.UtcNow;
        var record = new KnowledgeBaseRecord(
            Guid.NewGuid(),
            request.TenantId.Trim(),
            request.WorkspaceId.Trim(),
            request.Name.Trim(),
            request.Description?.Trim(),
            request.DefaultVisibility,
            now);

        knowledgeBases[record.Id] = record;
        AddAudit(record.TenantId, record.WorkspaceId, "knowledge_base.created", "KnowledgeBase", record.Id, null, null, ToJson(record));

        return Task.FromResult(record.ToDto());
    }

    /// <summary>
    /// 按租户和工作空间过滤知识库列表。
    /// </summary>
    public Task<IReadOnlyCollection<KnowledgeBaseDto>> ListKnowledgeBasesAsync(
        string tenantId,
        string? workspaceId,
        CancellationToken cancellationToken = default)
    {
        var result = knowledgeBases.Values
            .Where(kb => kb.TenantId == tenantId && (workspaceId is null || kb.WorkspaceId == workspaceId))
            .OrderByDescending(kb => kb.CreatedAt)
            .Select(kb => kb.ToDto())
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<KnowledgeBaseDto>>(result);
    }

    /// <summary>
    /// 查询单个知识库。
    /// </summary>
    public Task<KnowledgeBaseDto?> GetKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(knowledgeBases.TryGetValue(knowledgeBaseId, out var record) ? record.ToDto() : null);
    }

    /// <summary>
    /// 上传文档并同步执行解析、分片和元数据写入。
    /// </summary>
    public async Task<DocumentDto> UploadDocumentAsync(
        Guid knowledgeBaseId,
        string fileName,
        string contentType,
        Stream content,
        UploadDocumentOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!knowledgeBases.TryGetValue(knowledgeBaseId, out var knowledgeBase))
        {
            throw new KeyNotFoundException("Knowledge base was not found.");
        }

        EnsureSameScope(knowledgeBase, options.TenantId, options.WorkspaceId);

        var now = DateTimeOffset.UtcNow;
        var document = new DocumentRecord(
            Guid.NewGuid(),
            options.TenantId.Trim(),
            options.WorkspaceId.Trim(),
            knowledgeBaseId,
            fileName,
            DocumentSourceType.Upload,
            $"memory://documents/{Guid.NewGuid()}-{fileName}",
            DocumentStatus.Processing,
            options.Visibility,
            NormalizeTags(options.Tags),
            NormalizeOptional(options.DirectoryPath),
            1,
            null,
            now,
            now);

        documents[document.Id] = document;
        ReplaceAcl(document.Id, options.Acl);
        AddAudit(document.TenantId, document.WorkspaceId, "document.uploaded", "Document", document.Id, null, null, ToJson(document));

        try
        {
            // 内存版和 PG 版共用同一条文档处理管线，保证行为一致。
            var text = await extractor.ExtractAsync(fileName, contentType, content, cancellationToken);
            var parts = chunker.Chunk(text);

            if (parts.Count == 0)
            {
                throw new InvalidOperationException("The document did not contain extractable text.");
            }

            var aclHash = ComputeAclHash(document.Visibility, aclByDocument[document.Id]);
            // Chunk 继承文档权限，通过 acl_hash 把权限快照同步给检索层。
            var chunkRecords = parts
                .Select((part, index) => new DocumentChunkRecord(
                    Guid.NewGuid(),
                    document.TenantId,
                    document.WorkspaceId,
                    document.KnowledgeBaseId,
                    document.Id,
                    index,
                    part,
                    ComputeHash(part),
                    embeddingGenerator.GenerateEmbeddingId(part),
                    aclHash,
                    DateTimeOffset.UtcNow))
                .ToList();

            chunksByDocument[document.Id] = chunkRecords;
            document = document with
            {
                Status = DocumentStatus.Completed,
                ChunkCount = chunkRecords.Count,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // 处理失败仍保留文档记录和失败原因，便于后续重试。
            document = document with
            {
                Status = DocumentStatus.Failed,
                FailureReason = ex.Message,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            chunksByDocument[document.Id] = [];
        }

        documents[document.Id] = document;
        AddAudit(document.TenantId, document.WorkspaceId, "document.processed", "Document", document.Id, null, null, ToJson(document));

        return document.ToDto();
    }

    /// <summary>
    /// 查询知识库下文档，并按标签/目录做可选过滤。
    /// </summary>
    public Task<IReadOnlyCollection<DocumentDto>> ListDocumentsAsync(
        Guid knowledgeBaseId,
        string tenantId,
        string workspaceId,
        string? tag,
        string? directoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedTag = NormalizeOptional(tag);
        var normalizedDirectory = NormalizeOptional(directoryPath);
        var result = documents.Values
            .Where(d => d.KnowledgeBaseId == knowledgeBaseId && d.TenantId == tenantId && d.WorkspaceId == workspaceId)
            .Where(d => normalizedTag is null || d.Tags.Contains(normalizedTag, StringComparer.OrdinalIgnoreCase))
            .Where(d => normalizedDirectory is null || d.DirectoryPath == normalizedDirectory)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.ToDto())
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<DocumentDto>>(result);
    }

    /// <summary>
    /// 查询文档详情。
    /// </summary>
    public Task<DocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(documents.TryGetValue(documentId, out var document) ? document.ToDto() : null);
    }

    /// <summary>
    /// 查询文档处理状态。
    /// </summary>
    public Task<DocumentProcessingStatusDto?> GetProcessingStatusAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        if (!documents.TryGetValue(documentId, out var document))
        {
            return Task.FromResult<DocumentProcessingStatusDto?>(null);
        }

        return Task.FromResult<DocumentProcessingStatusDto?>(new DocumentProcessingStatusDto(
            document.Id,
            document.Status,
            document.ChunkCount,
            document.FailureReason,
            document.UpdatedAt));
    }

    /// <summary>
    /// 查询文档分片预览。
    /// </summary>
    public Task<IReadOnlyCollection<DocumentChunkDto>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var result = chunksByDocument.TryGetValue(documentId, out var chunks)
            ? chunks.OrderBy(c => c.Index).Select(c => c.ToDto()).ToArray()
            : [];

        return Task.FromResult<IReadOnlyCollection<DocumentChunkDto>>(result);
    }

    /// <summary>
    /// 查询文档 ACL。
    /// </summary>
    public Task<IReadOnlyCollection<DocumentAclEntryDto>> GetAclAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var result = aclByDocument.TryGetValue(documentId, out var entries)
            ? entries.Select(e => e.ToDto()).ToArray()
            : [];

        return Task.FromResult<IReadOnlyCollection<DocumentAclEntryDto>>(result);
    }

    /// <summary>
    /// 更新文档 ACL，并同步刷新所有分片的 acl_hash。
    /// </summary>
    public Task<DocumentDto?> UpdateAclAsync(
        Guid documentId,
        UpdateDocumentAclRequest request,
        string? actorId,
        CancellationToken cancellationToken = default)
    {
        if (!documents.TryGetValue(documentId, out var document))
        {
            return Task.FromResult<DocumentDto?>(null);
        }

        var before = new
        {
            document.Visibility,
            Acl = aclByDocument.TryGetValue(documentId, out var beforeAcl) ? beforeAcl.Select(a => a.ToDto()).ToArray() : []
        };

        ReplaceAcl(documentId, request.Acl);
        var updated = document with
        {
            Visibility = request.Visibility,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        documents[documentId] = updated;
        UpdateChunkAclHash(updated);

        var after = new
        {
            updated.Visibility,
            Acl = aclByDocument.TryGetValue(documentId, out var afterAcl) ? afterAcl.Select(a => a.ToDto()).ToArray() : []
        };

        AddAudit(updated.TenantId, updated.WorkspaceId, "document_acl.updated", "Document", updated.Id, actorId, ToJson(before), ToJson(after));

        return Task.FromResult<DocumentDto?>(updated.ToDto());
    }

    /// <summary>
    /// 查询审计日志，限制返回最近 200 条。
    /// </summary>
    public Task<IReadOnlyCollection<AuditLogDto>> GetAuditLogsAsync(
        string tenantId,
        Guid? entityId,
        CancellationToken cancellationToken = default)
    {
        var result = auditLogs
            .Where(log => log.TenantId == tenantId && (entityId is null || log.EntityId == entityId))
            .OrderByDescending(log => log.CreatedAt)
            .Take(200)
            .Select(log => log.ToDto())
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<AuditLogDto>>(result);
    }

    /// <summary>
    /// 用新 ACL 完整替换旧 ACL。
    /// </summary>
    void ReplaceAcl(Guid documentId, IReadOnlyCollection<UpsertDocumentAclEntry> acl)
    {
        aclByDocument[documentId] = acl
            .Where(entry => !string.IsNullOrWhiteSpace(entry.PrincipalId))
            .Select(entry => new DocumentAclEntryRecord(Guid.NewGuid(), entry.PrincipalType, entry.PrincipalId.Trim(), entry.Permission))
            .ToList();
    }

    /// <summary>
    /// 文档权限变更后刷新分片权限快照。
    /// </summary>
    void UpdateChunkAclHash(DocumentRecord document)
    {
        if (!chunksByDocument.TryGetValue(document.Id, out var chunks))
        {
            return;
        }

        var acl = aclByDocument.TryGetValue(document.Id, out var entries) ? entries : [];
        var aclHash = ComputeAclHash(document.Visibility, acl);
        chunksByDocument[document.Id] = chunks.Select(chunk => chunk with { AclHash = aclHash }).ToList();
    }

    /// <summary>
    /// 追加一条内存审计日志。
    /// </summary>
    void AddAudit(
        string tenantId,
        string workspaceId,
        string action,
        string entityType,
        Guid entityId,
        string? actorId,
        string? before,
        string? after)
    {
        auditLogs.Enqueue(new AuditLogRecord(
            Guid.NewGuid(),
            tenantId,
            workspaceId,
            action,
            entityType,
            entityId,
            actorId,
            before,
            after,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// 确保文档上传范围与知识库租户/工作空间一致。
    /// </summary>
    static void EnsureSameScope(KnowledgeBaseRecord knowledgeBase, string tenantId, string workspaceId)
    {
        if (knowledgeBase.TenantId != tenantId || knowledgeBase.WorkspaceId != workspaceId)
        {
            throw new InvalidOperationException("The document scope must match the knowledge base tenant and workspace.");
        }
    }

    /// <summary>
    /// 规范化标签，去空、去重并稳定排序。
    /// </summary>
    static string[] NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(NormalizeOptional)
            .Where(tag => tag is not null)
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 规范化可选字符串，空白值统一为 null。
    /// </summary>
    static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 根据文档可见性和 ACL 生成权限快照哈希。
    /// </summary>
    static string ComputeAclHash(DocumentVisibility visibility, IEnumerable<DocumentAclEntryRecord> acl)
    {
        var payload = new
        {
            visibility,
            acl = acl
                .OrderBy(entry => entry.PrincipalType)
                .ThenBy(entry => entry.PrincipalId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Permission)
                .Select(entry => new { entry.PrincipalType, entry.PrincipalId, entry.Permission })
        };

        return ComputeHash(ToJson(payload));
    }

    /// <summary>
    /// 生成 SHA-256 哈希字符串。
    /// </summary>
    static string ComputeHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 使用 Web 默认 JSON 风格序列化审计快照。
    /// </summary>
    static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

/// <summary>
/// 内存知识库记录。
/// </summary>
internal sealed record KnowledgeBaseRecord(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string Name,
    string? Description,
    KnowledgeBaseVisibility DefaultVisibility,
    DateTimeOffset CreatedAt)
{
    /// <summary>转换为对外 DTO。</summary>
    public KnowledgeBaseDto ToDto() => new(Id, TenantId, WorkspaceId, Name, Description, DefaultVisibility, CreatedAt);
}

/// <summary>
/// 内存文档记录。
/// </summary>
internal sealed record DocumentRecord(
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
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>文档分片数量。</summary>
    public int ChunkCount { get; init; }

    /// <summary>转换为对外 DTO。</summary>
    public DocumentDto ToDto() => new(
        Id,
        TenantId,
        WorkspaceId,
        KnowledgeBaseId,
        Name,
        SourceType,
        FileUri,
        Status,
        Visibility,
        Tags,
        DirectoryPath,
        Version,
        ChunkCount,
        FailureReason,
        CreatedAt,
        UpdatedAt);
}

/// <summary>
/// 内存 ACL 记录。
/// </summary>
internal sealed record DocumentAclEntryRecord(
    Guid Id,
    AclPrincipalType PrincipalType,
    string PrincipalId,
    DocumentPermission Permission)
{
    /// <summary>转换为对外 DTO。</summary>
    public DocumentAclEntryDto ToDto() => new(Id, PrincipalType, PrincipalId, Permission);
}

/// <summary>
/// 内存分片记录。
/// </summary>
internal sealed record DocumentChunkRecord(
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
    DateTimeOffset CreatedAt)
{
    /// <summary>转换为对外 DTO。</summary>
    public DocumentChunkDto ToDto() => new(
        Id,
        TenantId,
        WorkspaceId,
        KnowledgeBaseId,
        DocumentId,
        Index,
        Content,
        ContentHash,
        EmbeddingId,
        AclHash,
        CreatedAt);
}

/// <summary>
/// 内存审计日志记录。
/// </summary>
internal sealed record AuditLogRecord(
    Guid Id,
    string TenantId,
    string WorkspaceId,
    string Action,
    string EntityType,
    Guid EntityId,
    string? ActorId,
    string? Before,
    string? After,
    DateTimeOffset CreatedAt)
{
    /// <summary>转换为对外 DTO。</summary>
    public AuditLogDto ToDto() => new(Id, TenantId, WorkspaceId, Action, EntityType, EntityId, ActorId, Before, After, CreatedAt);
}
