CREATE TABLE IF NOT EXISTS channel_configs (
    id UUID PRIMARY KEY,
    tenant_id TEXT NOT NULL,
    workspace_id TEXT NOT NULL,
    application_id TEXT,
    type INTEGER NOT NULL,
    name TEXT NOT NULL,
    status INTEGER NOT NULL,
    widget JSONB NOT NULL DEFAULT '{}'::jsonb,
    settings JSONB NOT NULL DEFAULT '{}'::jsonb,
    credentials JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE channel_configs IS '对话渠道配置表，保存 Web Widget、REST API 以及后续 IM 渠道的接入配置。';
COMMENT ON COLUMN channel_configs.tenant_id IS '租户标识，是渠道配置的数据隔离边界。';
COMMENT ON COLUMN channel_configs.workspace_id IS '工作空间标识，是业务线或部门级隔离边界。';
COMMENT ON COLUMN channel_configs.application_id IS '可选应用标识，用于将渠道绑定到具体客服应用。';
COMMENT ON COLUMN channel_configs.type IS '渠道类型枚举。';
COMMENT ON COLUMN channel_configs.status IS '渠道生命周期状态。';
COMMENT ON COLUMN channel_configs.widget IS 'Widget 品牌和启动配置 JSON。';
COMMENT ON COLUMN channel_configs.settings IS '非敏感渠道设置 JSON。';
COMMENT ON COLUMN channel_configs.credentials IS '敏感渠道凭证 JSON，API 查询时不会返回原文。';

CREATE INDEX IF NOT EXISTS idx_channel_configs_scope
    ON channel_configs(tenant_id, workspace_id, application_id, type);

CREATE INDEX IF NOT EXISTS idx_channel_configs_status
    ON channel_configs(tenant_id, status, updated_at DESC);

CREATE TABLE IF NOT EXISTS channel_webhook_subscriptions (
    id UUID PRIMARY KEY,
    channel_config_id UUID NOT NULL REFERENCES channel_configs(id) ON DELETE CASCADE,
    event_type INTEGER NOT NULL,
    url TEXT NOT NULL,
    secret TEXT,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_by UUID,
    created_at TIMESTAMP WITHOUT TIME ZONE,
    updated_by UUID,
    updated_at TIMESTAMP WITHOUT TIME ZONE,
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE
);

COMMENT ON TABLE channel_webhook_subscriptions IS '渠道 Webhook 订阅表，保存外部系统事件推送配置。';
COMMENT ON COLUMN channel_webhook_subscriptions.event_type IS '订阅的渠道事件类型。';
COMMENT ON COLUMN channel_webhook_subscriptions.secret IS '可选签名密钥，API 查询时不会返回原文。';

CREATE INDEX IF NOT EXISTS idx_channel_webhooks_channel
    ON channel_webhook_subscriptions(channel_config_id);

CREATE INDEX IF NOT EXISTS idx_channel_webhooks_event
    ON channel_webhook_subscriptions(event_type, enabled);
