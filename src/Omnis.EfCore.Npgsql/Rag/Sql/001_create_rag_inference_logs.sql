-- RAG 推理观测表：记录问答过程中的检索、Prompt、输出和置信度。
CREATE TABLE IF NOT EXISTS rag_inference_logs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    application_id TEXT,
    conversation_id TEXT,
    message_id TEXT,
    user_id TEXT NOT NULL,
    user_question TEXT NOT NULL,
    rewritten_query TEXT NOT NULL,
    retrieved_chunks JSONB NOT NULL DEFAULT '[]'::jsonb,
    final_prompt TEXT NOT NULL,
    llm_raw_output TEXT NOT NULL,
    final_answer TEXT NOT NULL,
    confidence_score NUMERIC(5,4) NOT NULL,
    citation_source_ids TEXT[] NOT NULL DEFAULT '{}',
    has_hallucination BOOLEAN NOT NULL DEFAULT FALSE,
    retrieval_duration_ms INTEGER NOT NULL DEFAULT 0,
    generation_duration_ms INTEGER NOT NULL DEFAULT 0,
    inference_duration_ms INTEGER NOT NULL DEFAULT 0,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE rag_inference_logs IS 'RAG 推理观测表，记录问答链路中的检索结果、Prompt、LLM 输出和置信度。';
COMMENT ON COLUMN rag_inference_logs.id IS '观测日志主键。';
COMMENT ON COLUMN rag_inference_logs.tenant_id IS '租户标识。';
COMMENT ON COLUMN rag_inference_logs.workspace_id IS '工作空间标识。';
COMMENT ON COLUMN rag_inference_logs.application_id IS '应用 ID。';
COMMENT ON COLUMN rag_inference_logs.conversation_id IS '会话 ID。';
COMMENT ON COLUMN rag_inference_logs.message_id IS '消息 ID。';
COMMENT ON COLUMN rag_inference_logs.user_id IS '提问用户 ID。';
COMMENT ON COLUMN rag_inference_logs.user_question IS '用户原始问题。';
COMMENT ON COLUMN rag_inference_logs.rewritten_query IS '查询改写后的检索问题。';
COMMENT ON COLUMN rag_inference_logs.retrieved_chunks IS '检索到的分片列表，JSONB。';
COMMENT ON COLUMN rag_inference_logs.final_prompt IS '最终发送给 LLM 的 Prompt。';
COMMENT ON COLUMN rag_inference_logs.llm_raw_output IS 'LLM 原始输出。';
COMMENT ON COLUMN rag_inference_logs.final_answer IS '最终返回给用户的答案。';
COMMENT ON COLUMN rag_inference_logs.confidence_score IS '置信度分数。';
COMMENT ON COLUMN rag_inference_logs.citation_source_ids IS '答案引用来源 ID 数组。';
COMMENT ON COLUMN rag_inference_logs.has_hallucination IS '是否检测到幻觉或引用异常。';
COMMENT ON COLUMN rag_inference_logs.retrieval_duration_ms IS '检索耗时，毫秒。';
COMMENT ON COLUMN rag_inference_logs.generation_duration_ms IS '生成耗时，毫秒。';
COMMENT ON COLUMN rag_inference_logs.inference_duration_ms IS '完整推理耗时，毫秒。';
COMMENT ON COLUMN rag_inference_logs.created_at IS '创建时间。';

CREATE INDEX IF NOT EXISTS idx_rag_logs_tenant_time
    ON rag_inference_logs(tenant_id, created_at DESC);

COMMENT ON INDEX idx_rag_logs_tenant_time IS '按租户和时间查询 RAG 观测日志。';

CREATE INDEX IF NOT EXISTS idx_rag_logs_confidence
    ON rag_inference_logs(confidence_score)
    WHERE confidence_score < 0.6;

COMMENT ON INDEX idx_rag_logs_confidence IS '低置信度 RAG 观测日志索引，用于问题排查和告警。';
