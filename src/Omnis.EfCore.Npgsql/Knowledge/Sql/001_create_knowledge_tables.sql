-- 知识库主表：表示租户和工作空间下的一组知识文档集合。
CREATE TABLE IF NOT EXISTS knowledge_bases (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    default_visibility INTEGER NOT NULL,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE knowledge_bases IS '知识库主表，按租户和工作空间隔离知识集合。';
COMMENT ON COLUMN knowledge_bases.id IS '知识库主键。';
COMMENT ON COLUMN knowledge_bases.tenant_id IS '租户标识，平台级数据隔离边界。';
COMMENT ON COLUMN knowledge_bases.workspace_id IS '工作空间标识，业务线或部门级数据隔离边界。';
COMMENT ON COLUMN knowledge_bases.name IS '知识库名称。';
COMMENT ON COLUMN knowledge_bases.description IS '知识库描述。';
COMMENT ON COLUMN knowledge_bases.default_visibility IS '知识库默认可见性策略，对应 KnowledgeBaseVisibility 枚举。';
COMMENT ON COLUMN knowledge_bases.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN knowledge_bases.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN knowledge_bases.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN knowledge_bases.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN knowledge_bases.is_deleted IS '软删除标记。';

CREATE INDEX IF NOT EXISTS idx_knowledge_bases_scope
    ON knowledge_bases(tenant_id, workspace_id, created_at);

COMMENT ON INDEX idx_knowledge_bases_scope IS '按租户、工作空间和创建时间查询知识库列表。';

-- 知识文档表：保存上传文档、状态、权限可见性和分类元数据。
CREATE TABLE IF NOT EXISTS knowledge_documents (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    knowledge_base_id UUID NOT NULL,
    name TEXT NOT NULL,
    source_type INTEGER NOT NULL,
    file_uri TEXT,
    status INTEGER NOT NULL,
    visibility INTEGER NOT NULL,
    tags TEXT[] NOT NULL DEFAULT '{}',
    directory_path TEXT,
    version INTEGER NOT NULL DEFAULT 1,
    chunk_count INTEGER NOT NULL DEFAULT 0,
    failure_reason TEXT,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_knowledge_documents_knowledge_bases_knowledge_base_id
        FOREIGN KEY (knowledge_base_id)
        REFERENCES knowledge_bases(id)
        ON DELETE CASCADE
);

COMMENT ON TABLE knowledge_documents IS '知识文档表，保存文档元数据、处理状态、可见性和分类信息。';
COMMENT ON COLUMN knowledge_documents.id IS '文档主键。';
COMMENT ON COLUMN knowledge_documents.tenant_id IS '租户标识。';
COMMENT ON COLUMN knowledge_documents.workspace_id IS '工作空间标识。';
COMMENT ON COLUMN knowledge_documents.knowledge_base_id IS '所属知识库 ID。';
COMMENT ON COLUMN knowledge_documents.name IS '文档名称。';
COMMENT ON COLUMN knowledge_documents.source_type IS '文档来源类型，对应 DocumentSourceType 枚举。';
COMMENT ON COLUMN knowledge_documents.file_uri IS '原始文件或外部来源地址。';
COMMENT ON COLUMN knowledge_documents.status IS '文档处理状态，对应 DocumentStatus 枚举。';
COMMENT ON COLUMN knowledge_documents.visibility IS '文档可见性策略，对应 DocumentVisibility 枚举。';
COMMENT ON COLUMN knowledge_documents.tags IS '文档标签数组，用于分类筛选。';
COMMENT ON COLUMN knowledge_documents.directory_path IS '文档目录路径。';
COMMENT ON COLUMN knowledge_documents.version IS '文档版本号。';
COMMENT ON COLUMN knowledge_documents.chunk_count IS '已生成的分片数量。';
COMMENT ON COLUMN knowledge_documents.failure_reason IS '处理失败原因。';
COMMENT ON COLUMN knowledge_documents.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN knowledge_documents.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN knowledge_documents.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN knowledge_documents.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN knowledge_documents.is_deleted IS '软删除标记。';

CREATE INDEX IF NOT EXISTS idx_knowledge_documents_scope
    ON knowledge_documents(tenant_id, workspace_id, knowledge_base_id, updated_at);

COMMENT ON INDEX idx_knowledge_documents_scope IS '按租户、工作空间、知识库和更新时间查询文档列表。';

CREATE INDEX IF NOT EXISTS idx_knowledge_documents_tags
    ON knowledge_documents
    USING gin(tags);

COMMENT ON INDEX idx_knowledge_documents_tags IS '文档标签 GIN 索引，用于标签筛选。';

-- 文档 ACL 表：保存文档级授权主体和权限，用于检索阶段强制过滤。
CREATE TABLE IF NOT EXISTS document_acl_entries (
    id UUID PRIMARY KEY,
    document_id UUID NOT NULL,
    principal_type INTEGER NOT NULL,
    principal_id TEXT NOT NULL,
    permission INTEGER NOT NULL,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_document_acl_entries_knowledge_documents_document_id
        FOREIGN KEY (document_id)
        REFERENCES knowledge_documents(id)
        ON DELETE CASCADE
);

COMMENT ON TABLE document_acl_entries IS '文档 ACL 表，保存用户、用户组、角色对文档的授权。';
COMMENT ON COLUMN document_acl_entries.id IS 'ACL 条目主键。';
COMMENT ON COLUMN document_acl_entries.document_id IS '所属文档 ID。';
COMMENT ON COLUMN document_acl_entries.principal_type IS '授权主体类型，对应 AclPrincipalType 枚举。';
COMMENT ON COLUMN document_acl_entries.principal_id IS '授权主体 ID，例如用户 ID、用户组 ID 或角色 ID。';
COMMENT ON COLUMN document_acl_entries.permission IS '授予权限，对应 DocumentPermission 枚举。';
COMMENT ON COLUMN document_acl_entries.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN document_acl_entries.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN document_acl_entries.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN document_acl_entries.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN document_acl_entries.is_deleted IS '软删除标记。';

CREATE INDEX IF NOT EXISTS idx_document_acl_entries_document
    ON document_acl_entries(document_id);

COMMENT ON INDEX idx_document_acl_entries_document IS '按文档 ID 查询 ACL 条目。';

-- 文档分片表：保存 RAG 检索和引用溯源的最小文本单元。
CREATE TABLE IF NOT EXISTS document_chunks (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    knowledge_base_id UUID NOT NULL,
    document_id UUID NOT NULL,
    chunk_index INTEGER NOT NULL,
    content TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    embedding_id TEXT NOT NULL,
    acl_hash TEXT NOT NULL,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_document_chunks_knowledge_documents_document_id
        FOREIGN KEY (document_id)
        REFERENCES knowledge_documents(id)
        ON DELETE CASCADE
);

COMMENT ON TABLE document_chunks IS '文档分片表，保存 RAG 检索、Prompt 组装和引用溯源的最小文本单元。';
COMMENT ON COLUMN document_chunks.id IS '分片主键。';
COMMENT ON COLUMN document_chunks.tenant_id IS '租户标识。';
COMMENT ON COLUMN document_chunks.workspace_id IS '工作空间标识。';
COMMENT ON COLUMN document_chunks.knowledge_base_id IS '所属知识库 ID。';
COMMENT ON COLUMN document_chunks.document_id IS '所属文档 ID。';
COMMENT ON COLUMN document_chunks.chunk_index IS '分片在文档内的顺序号。';
COMMENT ON COLUMN document_chunks.content IS '分片文本内容。';
COMMENT ON COLUMN document_chunks.content_hash IS '分片内容哈希，用于幂等处理和变更识别。';
COMMENT ON COLUMN document_chunks.embedding_id IS 'Embedding 标识，用于追踪向量化版本或内容指纹。';
COMMENT ON COLUMN document_chunks.acl_hash IS '权限快照哈希，ACL 变更后用于同步检索元数据。';
COMMENT ON COLUMN document_chunks.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN document_chunks.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN document_chunks.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN document_chunks.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN document_chunks.is_deleted IS '软删除标记。';

CREATE UNIQUE INDEX IF NOT EXISTS ix_document_chunks_document_id_chunk_index
    ON document_chunks(document_id, chunk_index);

COMMENT ON INDEX ix_document_chunks_document_id_chunk_index IS '保证同一文档内分片序号唯一。';

CREATE INDEX IF NOT EXISTS idx_document_chunks_scope
    ON document_chunks(tenant_id, workspace_id, knowledge_base_id, document_id);

COMMENT ON INDEX idx_document_chunks_scope IS '按租户、工作空间、知识库和文档定位分片。';

CREATE INDEX IF NOT EXISTS idx_document_chunks_acl
    ON document_chunks(acl_hash);

COMMENT ON INDEX idx_document_chunks_acl IS '按权限快照哈希查询或同步分片。';

-- 知识向量表：保存分片对应的向量和检索过滤元数据。
CREATE TABLE IF NOT EXISTS knowledge_vectors (
    chunk_id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    knowledge_base_id UUID NOT NULL,
    document_id UUID NOT NULL,
    content_hash TEXT NOT NULL,
    embedding_id TEXT NOT NULL,
    acl_hash TEXT NOT NULL,
    vector DOUBLE PRECISION[] NOT NULL DEFAULT '{}',
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT fk_knowledge_vectors_document_chunks_chunk_id
        FOREIGN KEY (chunk_id)
        REFERENCES document_chunks(id)
        ON DELETE CASCADE
);

COMMENT ON TABLE knowledge_vectors IS '知识向量表，保存分片向量及检索阶段需要的租户、空间、知识库和权限元数据。';
COMMENT ON COLUMN knowledge_vectors.chunk_id IS '分片 ID，同时作为向量记录主键。';
COMMENT ON COLUMN knowledge_vectors.tenant_id IS '租户标识，用于检索阶段数据隔离。';
COMMENT ON COLUMN knowledge_vectors.workspace_id IS '工作空间标识，用于检索阶段数据隔离。';
COMMENT ON COLUMN knowledge_vectors.knowledge_base_id IS '所属知识库 ID。';
COMMENT ON COLUMN knowledge_vectors.document_id IS '所属文档 ID。';
COMMENT ON COLUMN knowledge_vectors.content_hash IS '分片内容哈希。';
COMMENT ON COLUMN knowledge_vectors.embedding_id IS 'Embedding 标识。';
COMMENT ON COLUMN knowledge_vectors.acl_hash IS '权限快照哈希，用于检索过滤元数据同步。';
COMMENT ON COLUMN knowledge_vectors.vector IS '分片向量，当前 PostgreSQL 默认实现使用 double precision 数组。';
COMMENT ON COLUMN knowledge_vectors.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN knowledge_vectors.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN knowledge_vectors.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN knowledge_vectors.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN knowledge_vectors.is_deleted IS '软删除标记。';

CREATE INDEX IF NOT EXISTS idx_knowledge_vectors_scope_acl
    ON knowledge_vectors(tenant_id, workspace_id, knowledge_base_id, acl_hash);

COMMENT ON INDEX idx_knowledge_vectors_scope_acl IS '向量检索阶段按租户、工作空间、知识库和权限快照过滤。';

-- 知识审计日志表：记录知识库、文档和权限等关键变更。
CREATE TABLE IF NOT EXISTS knowledge_audit_logs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    action TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id UUID NOT NULL,
    actor_id TEXT,
    before_json JSONB,
    after_json JSONB,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE knowledge_audit_logs IS '知识模块审计日志表，记录知识库、文档、ACL 等关键操作。';
COMMENT ON COLUMN knowledge_audit_logs.id IS '审计日志主键。';
COMMENT ON COLUMN knowledge_audit_logs.tenant_id IS '租户标识。';
COMMENT ON COLUMN knowledge_audit_logs.workspace_id IS '工作空间标识。';
COMMENT ON COLUMN knowledge_audit_logs.action IS '操作类型，例如 document.uploaded、document_acl.updated。';
COMMENT ON COLUMN knowledge_audit_logs.entity_type IS '操作对象类型，例如 KnowledgeBase、Document。';
COMMENT ON COLUMN knowledge_audit_logs.entity_id IS '操作对象 ID。';
COMMENT ON COLUMN knowledge_audit_logs.actor_id IS '操作者外部标识。';
COMMENT ON COLUMN knowledge_audit_logs.before_json IS '变更前快照，JSONB。';
COMMENT ON COLUMN knowledge_audit_logs.after_json IS '变更后快照，JSONB。';
COMMENT ON COLUMN knowledge_audit_logs.created_by IS '创建人用户 ID。';
COMMENT ON COLUMN knowledge_audit_logs.created_at IS '创建时间，UTC。';
COMMENT ON COLUMN knowledge_audit_logs.updated_by IS '最后更新人用户 ID。';
COMMENT ON COLUMN knowledge_audit_logs.updated_at IS '最后更新时间，UTC。';
COMMENT ON COLUMN knowledge_audit_logs.is_deleted IS '软删除标记。';

CREATE INDEX IF NOT EXISTS idx_knowledge_audit_logs_tenant_time
    ON knowledge_audit_logs(tenant_id, created_at);

COMMENT ON INDEX idx_knowledge_audit_logs_tenant_time IS '按租户和时间查询审计日志。';

CREATE INDEX IF NOT EXISTS idx_knowledge_audit_logs_entity
    ON knowledge_audit_logs(entity_id, created_at);

COMMENT ON INDEX idx_knowledge_audit_logs_entity IS '按业务对象和时间查询审计日志。';
