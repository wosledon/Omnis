using Microsoft.EntityFrameworkCore;
using Omnis.EfCore.Npgsql.Chat.Entities;
using Omnis.EfCore.Npgsql.Knowledge.Entities;
using Omnis.EfCore.Npgsql.Rag.Entities;
using Omnis.EfCore.Services;

namespace Omnis.EfCore.Npgsql;

/// <summary>
/// Npgsql 版本的 Omnis 数据上下文，集中声明知识模块实体和 PostgreSQL 表映射。
/// </summary>
public sealed class OmnisNpgsqlDbContext(
    DbContextOptions<OmnisNpgsqlDbContext> options,
    IAuditContextProvider? auditContextProvider = null
) : OmnisDbContext(options, auditContextProvider)
{
    /// <summary>知识库实体集合。</summary>
    public DbSet<KnowledgeBaseEntity> KnowledgeBases => Set<KnowledgeBaseEntity>();

    /// <summary>知识文档实体集合。</summary>
    public DbSet<KnowledgeDocumentEntity> KnowledgeDocuments => Set<KnowledgeDocumentEntity>();

    /// <summary>文档 ACL 实体集合。</summary>
    public DbSet<DocumentAclEntryEntity> DocumentAclEntries => Set<DocumentAclEntryEntity>();

    /// <summary>文档分片实体集合。</summary>
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();

    /// <summary>知识向量实体集合。</summary>
    public DbSet<KnowledgeVectorEntity> KnowledgeVectors => Set<KnowledgeVectorEntity>();

    /// <summary>知识模块审计日志实体集合。</summary>
    public DbSet<KnowledgeAuditLogEntity> KnowledgeAuditLogs => Set<KnowledgeAuditLogEntity>();

    /// <summary>RAG 推理观测日志实体集合。</summary>
    public DbSet<RagInferenceLogEntity> RagInferenceLogs => Set<RagInferenceLogEntity>();

    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();

    public DbSet<ConversationMessageEntity> ConversationMessages => Set<ConversationMessageEntity>();

    public DbSet<MessageFeedbackEntity> MessageFeedback => Set<MessageFeedbackEntity>();

    public DbSet<HumanHandoffEntity> HumanHandoffs => Set<HumanHandoffEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureKnowledgeBase(modelBuilder);
        ConfigureKnowledgeDocument(modelBuilder);
        ConfigureDocumentAclEntry(modelBuilder);
        ConfigureDocumentChunk(modelBuilder);
        ConfigureKnowledgeVector(modelBuilder);
        ConfigureKnowledgeAuditLog(modelBuilder);
        ConfigureRagInferenceLog(modelBuilder);
        ConfigureConversation(modelBuilder);
        ConfigureConversationMessage(modelBuilder);
        ConfigureMessageFeedback(modelBuilder);
        ConfigureHumanHandoff(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// 配置知识库表字段、索引和审计列。
    /// </summary>
    static void ConfigureKnowledgeBase(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeBaseEntity>();
        entity.ToTable("knowledge_bases");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.Name).HasColumnName("name").IsRequired();
        entity.Property(x => x.Description).HasColumnName("description");
        entity.Property(x => x.DefaultVisibility).HasColumnName("default_visibility").HasConversion<int>();
        entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt }).HasDatabaseName("idx_knowledge_bases_scope");
    }

    /// <summary>
    /// 配置知识文档表字段、数组标签和知识库外键。
    /// </summary>
    static void ConfigureKnowledgeDocument(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeDocumentEntity>();
        entity.ToTable("knowledge_documents");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.KnowledgeBaseId).HasColumnName("knowledge_base_id");
        entity.Property(x => x.Name).HasColumnName("name").IsRequired();
        entity.Property(x => x.SourceType).HasColumnName("source_type").HasConversion<int>();
        entity.Property(x => x.FileUri).HasColumnName("file_uri");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        entity.Property(x => x.Visibility).HasColumnName("visibility").HasConversion<int>();
        entity.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");
        entity.Property(x => x.DirectoryPath).HasColumnName("directory_path");
        entity.Property(x => x.Version).HasColumnName("version");
        entity.Property(x => x.ChunkCount).HasColumnName("chunk_count");
        entity.Property(x => x.FailureReason).HasColumnName("failure_reason");

        entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.KnowledgeBaseId, x.UpdatedAt }).HasDatabaseName("idx_knowledge_documents_scope");
        entity.HasIndex(x => x.Tags).HasDatabaseName("idx_knowledge_documents_tags").HasMethod("gin");
        entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// 配置文档 ACL 表字段和文档外键。
    /// </summary>
    static void ConfigureDocumentAclEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentAclEntryEntity>();
        entity.ToTable("document_acl_entries");
        ConfigureEntityBase(entity);

        entity.Property(x => x.DocumentId).HasColumnName("document_id");
        entity.Property(x => x.PrincipalType).HasColumnName("principal_type").HasConversion<int>();
        entity.Property(x => x.PrincipalId).HasColumnName("principal_id").IsRequired();
        entity.Property(x => x.Permission).HasColumnName("permission").HasConversion<int>();

        entity.HasIndex(x => x.DocumentId).HasDatabaseName("idx_document_acl_entries_document");
        entity.HasOne<KnowledgeDocumentEntity>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// 配置文档分片表字段、唯一索引和文档外键。
    /// </summary>
    static void ConfigureDocumentChunk(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<DocumentChunkEntity>();
        entity.ToTable("document_chunks");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.KnowledgeBaseId).HasColumnName("knowledge_base_id");
        entity.Property(x => x.DocumentId).HasColumnName("document_id");
        entity.Property(x => x.ChunkIndex).HasColumnName("chunk_index");
        entity.Property(x => x.Content).HasColumnName("content").IsRequired();
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").IsRequired();
        entity.Property(x => x.EmbeddingId).HasColumnName("embedding_id").IsRequired();
        entity.Property(x => x.AclHash).HasColumnName("acl_hash").IsRequired();

        entity.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
        entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.KnowledgeBaseId, x.DocumentId }).HasDatabaseName("idx_document_chunks_scope");
        entity.HasIndex(x => x.AclHash).HasDatabaseName("idx_document_chunks_acl");
        entity.HasOne<KnowledgeDocumentEntity>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// 配置 PostgreSQL 默认向量表，后续 Qdrant/Milvus 可复用同一实体负载结构。
    /// </summary>
    static void ConfigureKnowledgeVector(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeVectorEntity>();
        entity.ToTable("knowledge_vectors");
        entity.HasKey(x => x.ChunkId);

        entity.Property(x => x.ChunkId).HasColumnName("chunk_id");
        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.KnowledgeBaseId).HasColumnName("knowledge_base_id");
        entity.Property(x => x.DocumentId).HasColumnName("document_id");
        entity.Property(x => x.ContentHash).HasColumnName("content_hash").IsRequired();
        entity.Property(x => x.EmbeddingId).HasColumnName("embedding_id").IsRequired();
        entity.Property(x => x.AclHash).HasColumnName("acl_hash").IsRequired();
        entity.Property(x => x.Vector).HasColumnName("vector").HasColumnType("double precision[]");
        entity.Property(x => x.CreatedBy).HasColumnName("created_by");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.IsDeleted).HasColumnName("is_deleted");

        entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.KnowledgeBaseId, x.AclHash }).HasDatabaseName("idx_knowledge_vectors_scope_acl");
        entity.HasOne<DocumentChunkEntity>().WithOne().HasForeignKey<KnowledgeVectorEntity>(x => x.ChunkId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// 配置知识模块审计日志表字段，JSON 快照使用 PostgreSQL jsonb 类型保存。
    /// </summary>
    static void ConfigureKnowledgeAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<KnowledgeAuditLogEntity>();
        entity.ToTable("knowledge_audit_logs");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.Action).HasColumnName("action").IsRequired();
        entity.Property(x => x.EntityType).HasColumnName("entity_type").IsRequired();
        entity.Property(x => x.EntityId).HasColumnName("entity_id");
        entity.Property(x => x.ActorId).HasColumnName("actor_id");
        entity.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        entity.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");

        entity.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("idx_knowledge_audit_logs_tenant_time");
        entity.HasIndex(x => new { x.EntityId, x.CreatedAt }).HasDatabaseName("idx_knowledge_audit_logs_entity");
    }

    /// <summary>
    /// 配置 RAG 推理观测日志表，用于调试检索、Prompt、LLM 输出和置信度。
    /// </summary>
    static void ConfigureRagInferenceLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<RagInferenceLogEntity>();
        entity.ToTable("rag_inference_logs");
        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.ApplicationId).HasColumnName("application_id");
        entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
        entity.Property(x => x.MessageId).HasColumnName("message_id");
        entity.Property(x => x.UserId).HasColumnName("user_id");
        entity.Property(x => x.UserQuestion).HasColumnName("user_question");
        entity.Property(x => x.RewrittenQuery).HasColumnName("rewritten_query");
        entity.Property(x => x.RetrievedChunksJson).HasColumnName("retrieved_chunks").HasColumnType("jsonb");
        entity.Property(x => x.FinalPrompt).HasColumnName("final_prompt");
        entity.Property(x => x.LlmRawOutput).HasColumnName("llm_raw_output");
        entity.Property(x => x.FinalAnswer).HasColumnName("final_answer");
        entity.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasPrecision(5, 4);
        entity.Property(x => x.CitationSourceIds).HasColumnName("citation_source_ids").HasColumnType("text[]");
        entity.Property(x => x.HasHallucination).HasColumnName("has_hallucination");
        entity.Property(x => x.RetrievalDurationMs).HasColumnName("retrieval_duration_ms");
        entity.Property(x => x.GenerationDurationMs).HasColumnName("generation_duration_ms");
        entity.Property(x => x.InferenceDurationMs).HasColumnName("inference_duration_ms");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");

        entity.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("idx_rag_logs_tenant_time");
        entity.HasIndex(x => x.ConfidenceScore).HasDatabaseName("idx_rag_logs_confidence");
    }

    static void ConfigureConversation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ConversationEntity>();
        entity.ToTable("conversations");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.WorkspaceId).HasColumnName("workspace_id").IsRequired();
        entity.Property(x => x.ApplicationId).HasColumnName("application_id");
        entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(x => x.UserName).HasColumnName("user_name");
        entity.Property(x => x.UserGroups).HasColumnName("user_groups").HasColumnType("text[]");
        entity.Property(x => x.UserRoles).HasColumnName("user_roles").HasColumnType("text[]");
        entity.Property(x => x.Channel).HasColumnName("channel").IsRequired();
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        entity.Property(x => x.KnowledgeBaseIds).HasColumnName("knowledge_base_ids").HasColumnType("uuid[]");
        entity.Property(x => x.ClosedAt).HasColumnName("closed_at");

        entity.HasIndex(x => new { x.TenantId, x.WorkspaceId, x.CreatedAt }).HasDatabaseName("idx_conversations_scope_time");
        entity.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt }).HasDatabaseName("idx_conversations_user_time");
    }

    static void ConfigureConversationMessage(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ConversationMessageEntity>();
        entity.ToTable("conversation_messages");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
        entity.Property(x => x.Role).HasColumnName("role").HasConversion<int>();
        entity.Property(x => x.Content).HasColumnName("content").IsRequired();
        entity.Property(x => x.CitationsJson).HasColumnName("citations").HasColumnType("jsonb");
        entity.Property(x => x.ConfidenceScore).HasColumnName("confidence_score");
        entity.Property(x => x.RagInferenceLogId).HasColumnName("rag_inference_log_id");

        entity.HasIndex(x => new { x.TenantId, x.ConversationId, x.CreatedAt }).HasDatabaseName("idx_conversation_messages_conversation_time");
        entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }

    static void ConfigureMessageFeedback(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MessageFeedbackEntity>();
        entity.ToTable("message_feedback");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.MessageId).HasColumnName("message_id");
        entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
        entity.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        entity.Property(x => x.Rating).HasColumnName("rating").HasConversion<int>();
        entity.Property(x => x.Reason).HasColumnName("reason");
        entity.Property(x => x.RagInferenceLogId).HasColumnName("rag_inference_log_id");

        entity.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("idx_message_feedback_tenant_time");
        entity.HasIndex(x => x.MessageId).HasDatabaseName("idx_message_feedback_message");
        entity.HasOne<ConversationMessageEntity>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
    }

    static void ConfigureHumanHandoff(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<HumanHandoffEntity>();
        entity.ToTable("human_handoffs");
        ConfigureEntityBase(entity);

        entity.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        entity.Property(x => x.ConversationId).HasColumnName("conversation_id");
        entity.Property(x => x.TriggerType).HasColumnName("trigger_type").HasConversion<int>();
        entity.Property(x => x.SummaryJson).HasColumnName("summary").HasColumnType("jsonb");
        entity.Property(x => x.LastAiMessageId).HasColumnName("last_ai_message_id");
        entity.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        entity.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");

        entity.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt }).HasDatabaseName("idx_human_handoffs_queue");
        entity.HasIndex(x => x.ConversationId).HasDatabaseName("idx_human_handoffs_conversation");
        entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// 统一配置继承 EntityBase 的审计列和软删除列。
    /// </summary>
    static void ConfigureEntityBase<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : Omnis.EfCore.Contracts.EntityBase
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id");
        entity.Property(x => x.CreatedBy).HasColumnName("created_by");
        entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        entity.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        entity.Property(x => x.IsDeleted).HasColumnName("is_deleted");
    }
}
