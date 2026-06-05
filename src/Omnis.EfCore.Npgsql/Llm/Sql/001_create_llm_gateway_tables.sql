CREATE TABLE IF NOT EXISTS llm_model_configs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    application_id TEXT NULL,
    name TEXT NOT NULL,
    provider INTEGER NOT NULL,
    model TEXT NOT NULL,
    endpoint TEXT NOT NULL,
    deployment_name TEXT NULL,
    status INTEGER NOT NULL,
    priority INTEGER NOT NULL DEFAULT 100,
    fallback_model_config_id UUID NULL,
    timeout_seconds INTEGER NOT NULL DEFAULT 60,
    failure_threshold INTEGER NOT NULL DEFAULT 3,
    circuit_break_seconds INTEGER NOT NULL DEFAULT 60,
    prompt_token_price_per_1k NUMERIC(12, 6) NULL,
    completion_token_price_per_1k NUMERIC(12, 6) NULL,
    parameters JSONB NOT NULL DEFAULT '{}'::jsonb,
    credentials JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_by UUID NULL,
    created_at TIMESTAMP NULL,
    updated_by UUID NULL,
    updated_at TIMESTAMP NULL,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_llm_model_configs_route
    ON llm_model_configs (tenant_id, workspace_id, application_id, status, priority);

CREATE INDEX IF NOT EXISTS idx_llm_model_configs_fallback
    ON llm_model_configs (fallback_model_config_id);

CREATE TABLE IF NOT EXISTS llm_invocation_logs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    application_id TEXT NULL,
    model_config_id UUID NOT NULL,
    model_config_name TEXT NOT NULL,
    provider INTEGER NOT NULL,
    model TEXT NOT NULL,
    request JSONB NOT NULL DEFAULT '{}'::jsonb,
    response JSONB NOT NULL DEFAULT '{}'::jsonb,
    status INTEGER NOT NULL,
    used_fallback BOOLEAN NOT NULL DEFAULT FALSE,
    prompt_tokens INTEGER NOT NULL DEFAULT 0,
    completion_tokens INTEGER NOT NULL DEFAULT 0,
    total_tokens INTEGER NOT NULL DEFAULT 0,
    duration_ms BIGINT NOT NULL DEFAULT 0,
    error_message TEXT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_llm_invocation_logs_tenant_time
    ON llm_invocation_logs (tenant_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_llm_invocation_logs_scope_time
    ON llm_invocation_logs (tenant_id, workspace_id, application_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_llm_invocation_logs_model_time
    ON llm_invocation_logs (model_config_id, created_at DESC);

CREATE TABLE IF NOT EXISTS llm_circuit_breakers (
    model_config_id UUID PRIMARY KEY,
    state INTEGER NOT NULL DEFAULT 0,
    failure_count INTEGER NOT NULL DEFAULT 0,
    opened_until TIMESTAMP NULL,
    last_failure_at TIMESTAMP NULL,
    last_success_at TIMESTAMP NULL,
    updated_at TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_llm_circuit_breakers_state
    ON llm_circuit_breakers (state, opened_until);
