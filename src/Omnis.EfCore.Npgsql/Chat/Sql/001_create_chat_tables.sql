-- 对话会话表：保存租户、工作空间、渠道、用户身份快照和默认知识库范围。
-- 用户组/角色在创建会话时固化，后续调用 RAG 时用于文档 ACL 过滤。
CREATE TABLE IF NOT EXISTS conversations (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    application_id TEXT,
    user_id TEXT NOT NULL,
    user_name TEXT,
    user_groups TEXT[] NOT NULL DEFAULT '{}',
    user_roles TEXT[] NOT NULL DEFAULT '{}',
    channel TEXT NOT NULL,
    status INTEGER NOT NULL,
    knowledge_base_ids UUID[] NOT NULL DEFAULT '{}',
    closed_at TIMESTAMP WITHOUT TIME ZONE,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE conversations IS '对话会话表，记录会话生命周期、渠道、用户身份快照和默认知识库范围。';
COMMENT ON COLUMN conversations.tenant_id IS '租户标识，业务数据隔离第一层边界。';
COMMENT ON COLUMN conversations.workspace_id IS '工作空间标识，业务线或部门级隔离边界。';
COMMENT ON COLUMN conversations.application_id IS '应用标识，可用于模型配置、RAG 参数和渠道配置绑定。';
COMMENT ON COLUMN conversations.user_id IS '终端用户标识。';
COMMENT ON COLUMN conversations.user_name IS '终端用户展示名。';
COMMENT ON COLUMN conversations.user_groups IS '用户组快照，用于 RAG 检索 ACL 过滤。';
COMMENT ON COLUMN conversations.user_roles IS '用户角色快照，用于 RAG 检索 ACL 过滤。';
COMMENT ON COLUMN conversations.channel IS '会话来源渠道，例如 web_widget、rest_api。';
COMMENT ON COLUMN conversations.status IS '会话状态：0 active，1 closed，2 handoff。';
COMMENT ON COLUMN conversations.knowledge_base_ids IS '会话默认可检索知识库范围。';
COMMENT ON COLUMN conversations.closed_at IS '会话关闭时间。';

-- 管理后台常按租户、工作空间和最近更新时间查看会话列表。
CREATE INDEX IF NOT EXISTS idx_conversations_scope_time
    ON conversations(tenant_id, workspace_id, created_at DESC);

-- 终端用户恢复历史会话时使用。
CREATE INDEX IF NOT EXISTS idx_conversations_user_time
    ON conversations(tenant_id, user_id, created_at DESC);

-- 会话消息表：保存用户、AI、人工坐席和系统消息。
-- AI 回复会冗余保存引用、置信度和 RAG 观测日志 ID，便于前端展示和后台调试。
CREATE TABLE IF NOT EXISTS conversation_messages (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    role INTEGER NOT NULL,
    content TEXT NOT NULL,
    citations JSONB NOT NULL DEFAULT '[]'::jsonb,
    confidence_score DOUBLE PRECISION,
    rag_inference_log_id UUID,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE conversation_messages IS '会话消息表，保存用户消息、AI 回复、人工坐席消息和系统消息。';
COMMENT ON COLUMN conversation_messages.tenant_id IS '租户标识。';
COMMENT ON COLUMN conversation_messages.conversation_id IS '所属会话 ID。';
COMMENT ON COLUMN conversation_messages.role IS '消息角色：0 user，1 assistant，2 agent，3 system。';
COMMENT ON COLUMN conversation_messages.content IS '消息正文。';
COMMENT ON COLUMN conversation_messages.citations IS 'AI 回复引用来源 JSON，用户消息通常为空数组。';
COMMENT ON COLUMN conversation_messages.confidence_score IS 'AI 回复置信度。';
COMMENT ON COLUMN conversation_messages.rag_inference_log_id IS '关联 RAG 观测日志 ID，用于调试链路展开。';

-- 查询会话详情时按时间正序读取消息。
CREATE INDEX IF NOT EXISTS idx_conversation_messages_conversation_time
    ON conversation_messages(tenant_id, conversation_id, created_at);

-- 消息反馈表：记录用户对 AI 回复的点赞/点踩。
-- 反馈关联 message 和 rag_inference_log，支持后续低分回答闭环分析。
CREATE TABLE IF NOT EXISTS message_feedback (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    message_id UUID NOT NULL REFERENCES conversation_messages(id) ON DELETE CASCADE,
    conversation_id UUID,
    user_id TEXT NOT NULL,
    rating INTEGER NOT NULL,
    reason TEXT,
    rag_inference_log_id UUID,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE message_feedback IS '消息反馈表，记录点赞/点踩并关联消息与 RAG 观测日志。';
COMMENT ON COLUMN message_feedback.tenant_id IS '租户标识。';
COMMENT ON COLUMN message_feedback.message_id IS '被评价的消息 ID。';
COMMENT ON COLUMN message_feedback.conversation_id IS '所属会话 ID，冗余保存便于后台筛选。';
COMMENT ON COLUMN message_feedback.user_id IS '反馈用户 ID。';
COMMENT ON COLUMN message_feedback.rating IS '反馈结果：0 up，1 down。';
COMMENT ON COLUMN message_feedback.reason IS '反馈原因，可为标准原因码或自由文本。';
COMMENT ON COLUMN message_feedback.rag_inference_log_id IS '被评价 AI 消息对应的 RAG 观测日志 ID。';

-- 运营后台通常按租户和时间查看近期反馈。
CREATE INDEX IF NOT EXISTS idx_message_feedback_tenant_time
    ON message_feedback(tenant_id, created_at DESC);

-- 打开消息详情或去重检查时按消息 ID 查询反馈。
CREATE INDEX IF NOT EXISTS idx_message_feedback_message
    ON message_feedback(message_id);

-- 人工转接表：MVP 使用单队列模型。
-- 创建转接后，会话状态会切换为 handoff，AI 默认不再直接向用户外发。
CREATE TABLE IF NOT EXISTS human_handoffs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
    trigger_type INTEGER NOT NULL,
    summary JSONB NOT NULL DEFAULT '{}'::jsonb,
    last_ai_message_id UUID,
    status INTEGER NOT NULL,
    assigned_agent_id TEXT,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE human_handoffs IS '人工转接记录表，保存转接触发原因、摘要、状态和坐席分配信息。';
COMMENT ON COLUMN human_handoffs.tenant_id IS '租户标识。';
COMMENT ON COLUMN human_handoffs.conversation_id IS '所属会话 ID。';
COMMENT ON COLUMN human_handoffs.trigger_type IS '触发类型：0 user_request，1 low_confidence，2 negative_feedback，3 system。';
COMMENT ON COLUMN human_handoffs.summary IS '转人工摘要 JSON，供坐席接入前快速理解上下文。';
COMMENT ON COLUMN human_handoffs.last_ai_message_id IS '触发转接时关联的上一条 AI 消息。';
COMMENT ON COLUMN human_handoffs.status IS '转接状态：0 queued，1 assigned，2 resolved，3 cancelled。';
COMMENT ON COLUMN human_handoffs.assigned_agent_id IS '已分配坐席 ID。';

-- 坐席工作台拉取待处理队列时使用。
CREATE INDEX IF NOT EXISTS idx_human_handoffs_queue
    ON human_handoffs(tenant_id, status, created_at);

-- 会话详情页查看转接记录时使用。
CREATE INDEX IF NOT EXISTS idx_human_handoffs_conversation
    ON human_handoffs(conversation_id);
