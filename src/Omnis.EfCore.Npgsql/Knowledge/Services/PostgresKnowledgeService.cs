using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Omnis.Contracts.Knowledge;
using Omnis.DocumentX.Knowledge;
using Omnis.EfCore.Npgsql.Contracts;
using Omnis.EfCore.Npgsql.Knowledge.Entities;

namespace Omnis.EfCore.Npgsql.Knowledge.Services;

/// <summary>
/// PostgreSQL 版知识管理服务，所有数据库读写都通过 EF Core 实体完成。
/// </summary>
internal sealed class PostgresKnowledgeService(
    OmnisNpgsqlDbContext dbContext,
    IDocumentTextExtractor extractor,
    ITextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    IKnowledgeVectorizer vectorizer,
    IKnowledgeVectorStore vectorStore
) : IKnowledgeService
{
    // 审计快照使用统一 JSON 配置，保证 API 和数据库记录格式一致。
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 创建知识库，并在同一次保存中写入审计日志实体。
    /// </summary>
    public async Task<KnowledgeBaseDto> CreateKnowledgeBaseAsync(CreateKnowledgeBaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var now = DateTime.UtcNow;
        var entity = new KnowledgeBaseEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            WorkspaceId = request.WorkspaceId.Trim(),
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            DefaultVisibility = request.DefaultVisibility,
            CreatedAt = now,
            UpdatedAt = now
        };

        var result = ToKnowledgeBaseDto(entity);
        dbContext.KnowledgeBases.Add(entity);
        dbContext.KnowledgeAuditLogs.Add(CreateAuditLogEntity(result.TenantId, result.WorkspaceId, "knowledge_base.created", "KnowledgeBase", result.Id, null, null, ToJson(result)));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToKnowledgeBaseDto(entity);
    }

    /// <summary>
    /// 按租户和可选工作空间查询知识库。
    /// </summary>
    public async Task<IReadOnlyCollection<KnowledgeBaseDto>> ListKnowledgeBasesAsync(
        string tenantId,
        string? workspaceId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.KnowledgeBases
            .AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            query = query.Where(knowledgeBase => knowledgeBase.WorkspaceId == workspaceId);
        }

        var entities = await query
            .OrderByDescending(knowledgeBase => knowledgeBase.CreatedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToKnowledgeBaseDto).ToArray();
    }

    /// <summary>
    /// 按知识库 ID 查询详情。
    /// </summary>
    public async Task<KnowledgeBaseDto?> GetKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.KnowledgeBases
            .AsNoTracking()
            .FirstOrDefaultAsync(knowledgeBase => knowledgeBase.Id == knowledgeBaseId, cancellationToken);

        return entity is null ? null : ToKnowledgeBaseDto(entity);
    }

    /// <summary>
    /// 上传文档并完成处理链路：文档记录、ACL、解析、分片、向量和审计。
    /// </summary>
    public async Task<DocumentDto> UploadDocumentAsync(
        Guid knowledgeBaseId,
        string fileName,
        string contentType,
        Stream content,
        UploadDocumentOptions options,
        CancellationToken cancellationToken = default)
    {
        var knowledgeBase = await dbContext.KnowledgeBases
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == knowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Knowledge base was not found.");

        if (knowledgeBase.TenantId != options.TenantId || knowledgeBase.WorkspaceId != options.WorkspaceId)
        {
            throw new InvalidOperationException("The document scope must match the knowledge base tenant and workspace.");
        }

        var now = DateTime.UtcNow;
        var document = new KnowledgeDocumentEntity
        {
            Id = Guid.NewGuid(),
            TenantId = options.TenantId.Trim(),
            WorkspaceId = options.WorkspaceId.Trim(),
            KnowledgeBaseId = knowledgeBaseId,
            Name = fileName,
            SourceType = DocumentSourceType.Upload,
            FileUri = $"postgres://knowledge_documents/{Guid.NewGuid()}-{fileName}",
            Status = DocumentStatus.Processing,
            Visibility = options.Visibility,
            Tags = NormalizeTags(options.Tags),
            DirectoryPath = NormalizeOptional(options.DirectoryPath),
            Version = 1,
            ChunkCount = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        var aclEntries = NormalizeAcl(options.Acl);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // 文档元数据和 ACL 先落库，后续处理失败时仍可展示失败原因。
            dbContext.KnowledgeDocuments.Add(document);
            await ReplaceAclEntitiesAsync(document.Id, aclEntries, null, cancellationToken);
            dbContext.KnowledgeAuditLogs.Add(CreateAuditLogEntity(document.TenantId, document.WorkspaceId, "document.uploaded", "Document", document.Id, null, null, ToJson(ToDocumentDto(document))));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            var text = await extractor.ExtractAsync(fileName, contentType, content, cancellationToken);
            var parts = chunker.Chunk(text);
            if (parts.Count == 0)
            {
                throw new InvalidOperationException("The document did not contain extractable text.");
            }

            var aclHash = ComputeAclHash(document.Visibility, aclEntries);
            var chunkEntities = parts
                .Select((part, index) => CreateChunkEntity(document, index, part, aclHash))
                .ToArray();

            document.Status = DocumentStatus.Completed;
            document.ChunkCount = chunkEntities.Length;
            document.FailureReason = null;
            document.UpdatedAt = DateTime.UtcNow;

            await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                await RemoveDocumentChunksAsync(document.Id, cancellationToken);
                dbContext.DocumentChunks.AddRange(chunkEntities);
                dbContext.KnowledgeAuditLogs.Add(CreateAuditLogEntity(document.TenantId, document.WorkspaceId, "document.processed", "Document", document.Id, null, null, ToJson(ToDocumentDto(document))));
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            // 向量记录通过向量存储抽象写入，默认实现仍然是 EF Core 实体表。
            var vectors = chunkEntities
                .Select(chunk => new VectorRecord(
                    chunk.Id,
                    chunk.TenantId,
                    chunk.WorkspaceId,
                    chunk.KnowledgeBaseId,
                    chunk.DocumentId,
                    chunk.ContentHash,
                    chunk.EmbeddingId,
                    chunk.AclHash,
                    vectorizer.Vectorize(chunk.Content)))
                .ToArray();

            await vectorStore.UpsertAsync(vectors, cancellationToken);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            document.Status = DocumentStatus.Failed;
            document.FailureReason = ex.Message;
            document.ChunkCount = 0;
            document.UpdatedAt = DateTime.UtcNow;

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await RemoveDocumentChunksAsync(document.Id, cancellationToken);
            dbContext.KnowledgeAuditLogs.Add(CreateAuditLogEntity(document.TenantId, document.WorkspaceId, "document.processed", "Document", document.Id, null, null, ToJson(ToDocumentDto(document))));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return ToDocumentDto(document);
    }

    /// <summary>
    /// 查询知识库文档列表，支持标签和目录过滤。
    /// </summary>
    public async Task<IReadOnlyCollection<DocumentDto>> ListDocumentsAsync(
        Guid knowledgeBaseId,
        string tenantId,
        string workspaceId,
        string? tag,
        string? directoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedTag = NormalizeOptional(tag);
        var normalizedDirectory = NormalizeOptional(directoryPath);
        var query = dbContext.KnowledgeDocuments
            .AsNoTracking()
            .Where(document =>
                document.KnowledgeBaseId == knowledgeBaseId &&
                document.TenantId == tenantId &&
                document.WorkspaceId == workspaceId);

        if (normalizedTag is not null)
        {
            query = query.Where(document => document.Tags.Contains(normalizedTag));
        }

        if (normalizedDirectory is not null)
        {
            query = query.Where(document => document.DirectoryPath == normalizedDirectory);
        }

        var entities = await query
            .OrderByDescending(document => document.UpdatedAt)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToDocumentDto).ToArray();
    }

    /// <summary>
    /// 按文档 ID 查询文档。
    /// </summary>
    public async Task<DocumentDto?> GetDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(document => document.Id == documentId, cancellationToken);

        return entity is null ? null : ToDocumentDto(entity);
    }

    /// <summary>
    /// 查询文档处理状态。
    /// </summary>
    public async Task<DocumentProcessingStatusDto?> GetProcessingStatusAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(documentId, cancellationToken);
        return document is null
            ? null
            : new DocumentProcessingStatusDto(document.Id, document.Status, document.ChunkCount, document.FailureReason, document.UpdatedAt);
    }

    /// <summary>
    /// 查询文档分片，按分片序号保持原文顺序。
    /// </summary>
    public async Task<IReadOnlyCollection<DocumentChunkDto>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.DocumentChunks
            .AsNoTracking()
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToChunkDto).ToArray();
    }

    /// <summary>
    /// 查询文档 ACL。
    /// </summary>
    public async Task<IReadOnlyCollection<DocumentAclEntryDto>> GetAclAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var entities = await dbContext.DocumentAclEntries
            .AsNoTracking()
            .Where(acl => acl.DocumentId == documentId)
            .OrderBy(acl => acl.PrincipalType)
            .ThenBy(acl => acl.PrincipalId)
            .ThenBy(acl => acl.Permission)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToAclDto).ToArray();
    }

    /// <summary>
    /// 替换文档 ACL，并同步刷新 chunk 与 vector 的权限哈希。
    /// </summary>
    public async Task<DocumentDto?> UpdateAclAsync(
        Guid documentId,
        UpdateDocumentAclRequest request,
        string? actorId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(entity => entity.Id == documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var beforeAcl = await GetAclAsync(documentId, cancellationToken);
        var before = new { document.Visibility, Acl = beforeAcl };
        var actorGuid = ParseActorId(actorId);
        var aclEntries = NormalizeAcl(request.Acl);
        var aclHash = ComputeAclHash(request.Visibility, aclEntries);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await ReplaceAclEntitiesAsync(documentId, aclEntries, actorGuid, cancellationToken);

            document.Visibility = request.Visibility;
            document.UpdatedBy = actorGuid;
            document.UpdatedAt = DateTime.UtcNow;

            var chunks = await dbContext.DocumentChunks
                .Where(chunk => chunk.DocumentId == documentId)
                .ToArrayAsync(cancellationToken);
            foreach (var chunk in chunks)
            {
                chunk.AclHash = aclHash;
                chunk.UpdatedBy = actorGuid;
            }

            var after = new { document.Visibility, Acl = aclEntries };
            dbContext.KnowledgeAuditLogs.Add(CreateAuditLogEntity(document.TenantId, document.WorkspaceId, "document_acl.updated", "Document", documentId, actorId, ToJson(before), ToJson(after)));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // 向量权限元数据同样通过向量存储抽象刷新，便于未来切换 Qdrant/Milvus。
        await vectorStore.UpdateAclHashAsync(documentId, aclHash, actorGuid, cancellationToken);

        return ToDocumentDto(document);
    }

    /// <summary>
    /// 查询审计日志，最多返回最近 200 条。
    /// </summary>
    public async Task<IReadOnlyCollection<AuditLogDto>> GetAuditLogsAsync(
        string tenantId,
        Guid? entityId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.KnowledgeAuditLogs
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId);

        if (entityId.HasValue)
        {
            query = query.Where(log => log.EntityId == entityId.Value);
        }

        var logs = await query
            .OrderByDescending(log => log.CreatedAt)
            .Take(200)
            .ToArrayAsync(cancellationToken);

        return logs.Select(ToAuditLogDto).ToArray();
    }

    /// <summary>
    /// 删除某文档已有分片实体，用于重试或重新处理。
    /// </summary>
    async Task RemoveDocumentChunksAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var chunks = await dbContext.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .ToArrayAsync(cancellationToken);

        dbContext.DocumentChunks.RemoveRange(chunks);
    }

    /// <summary>
    /// 用新 ACL 完整替换旧 ACL 实体。
    /// </summary>
    async Task ReplaceAclEntitiesAsync(
        Guid documentId,
        IReadOnlyCollection<UpsertDocumentAclEntry> acl,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        var existingEntries = await dbContext.DocumentAclEntries
            .Where(entry => entry.DocumentId == documentId)
            .ToArrayAsync(cancellationToken);

        dbContext.DocumentAclEntries.RemoveRange(existingEntries);

        var now = DateTime.UtcNow;
        var entities = acl.Select(entry => new DocumentAclEntryEntity
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            PrincipalType = entry.PrincipalType,
            PrincipalId = entry.PrincipalId,
            Permission = entry.Permission,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedBy = actorId,
            UpdatedAt = now
        });

        dbContext.DocumentAclEntries.AddRange(entities);
    }

    /// <summary>
    /// 根据文档和分片文本创建分片实体。
    /// </summary>
    DocumentChunkEntity CreateChunkEntity(KnowledgeDocumentEntity document, int index, string content, string aclHash)
    {
        var now = DateTime.UtcNow;
        return new DocumentChunkEntity
        {
            Id = Guid.NewGuid(),
            TenantId = document.TenantId,
            WorkspaceId = document.WorkspaceId,
            KnowledgeBaseId = document.KnowledgeBaseId,
            DocumentId = document.Id,
            ChunkIndex = index,
            Content = content,
            ContentHash = ComputeHash(content),
            EmbeddingId = embeddingGenerator.GenerateEmbeddingId(content),
            AclHash = aclHash,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 创建审计日志实体。
    /// </summary>
    static KnowledgeAuditLogEntity CreateAuditLogEntity(
        string tenantId,
        string workspaceId,
        string action,
        string entityType,
        Guid entityId,
        string? actorId,
        string? before,
        string? after)
    {
        var now = DateTime.UtcNow;
        var actorGuid = ParseActorId(actorId);
        return new KnowledgeAuditLogEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorId = actorId,
            BeforeJson = before,
            AfterJson = after,
            CreatedBy = actorGuid,
            CreatedAt = now,
            UpdatedBy = actorGuid,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 将知识库实体转换为 API DTO。
    /// </summary>
    static KnowledgeBaseDto ToKnowledgeBaseDto(KnowledgeBaseEntity entity)
    {
        return new KnowledgeBaseDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.Name,
            entity.Description,
            entity.DefaultVisibility,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 将文档实体转换为 API DTO。
    /// </summary>
    static DocumentDto ToDocumentDto(KnowledgeDocumentEntity entity)
    {
        return new DocumentDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.KnowledgeBaseId,
            entity.Name,
            entity.SourceType,
            entity.FileUri,
            entity.Status,
            entity.Visibility,
            entity.Tags,
            entity.DirectoryPath,
            entity.Version,
            entity.ChunkCount,
            entity.FailureReason,
            ToDateTimeOffset(entity.CreatedAt),
            ToDateTimeOffset(entity.UpdatedAt ?? entity.CreatedAt));
    }

    /// <summary>
    /// 将 ACL 实体转换为 API DTO。
    /// </summary>
    static DocumentAclEntryDto ToAclDto(DocumentAclEntryEntity entity)
    {
        return new DocumentAclEntryDto(entity.Id, entity.PrincipalType, entity.PrincipalId, entity.Permission);
    }

    /// <summary>
    /// 将分片实体转换为 API DTO。
    /// </summary>
    static DocumentChunkDto ToChunkDto(DocumentChunkEntity entity)
    {
        return new DocumentChunkDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.KnowledgeBaseId,
            entity.DocumentId,
            entity.ChunkIndex,
            entity.Content,
            entity.ContentHash,
            entity.EmbeddingId,
            entity.AclHash,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 将审计日志实体转换为 API DTO。
    /// </summary>
    static AuditLogDto ToAuditLogDto(KnowledgeAuditLogEntity entity)
    {
        return new AuditLogDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.Action,
            entity.EntityType,
            entity.EntityId,
            entity.ActorId,
            entity.BeforeJson,
            entity.AfterJson,
            ToDateTimeOffset(entity.CreatedAt));
    }

    /// <summary>
    /// 规范化 ACL，过滤空主体并去重。
    /// </summary>
    static IReadOnlyCollection<UpsertDocumentAclEntry> NormalizeAcl(IEnumerable<UpsertDocumentAclEntry> entries)
    {
        return entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.PrincipalId))
            .Select(entry => entry with { PrincipalId = entry.PrincipalId.Trim() })
            .Distinct()
            .ToArray();
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
    /// 将外部传入的操作人标识转换为审计字段使用的 Guid。
    /// </summary>
    static Guid? ParseActorId(string? actorId)
    {
        return Guid.TryParse(actorId, out var value) ? value : null;
    }

    /// <summary>
    /// 根据文档可见性和 ACL 生成权限快照哈希。
    /// </summary>
    static string ComputeAclHash(DocumentVisibility visibility, IEnumerable<UpsertDocumentAclEntry> acl)
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
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// 将实体中的 UTC DateTime 转成 DTO 使用的 DateTimeOffset。
    /// </summary>
    static DateTimeOffset ToDateTimeOffset(DateTime? value)
    {
        var dateTime = value ?? DateTime.UtcNow;
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dateTime.ToUniversalTime());
    }
}
