using Omnis.Contracts.Channel;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Channel.Entities;

/// <summary>
/// 渠道 Webhook 订阅持久化实体，对应 channel_webhook_subscriptions 表。
/// </summary>
public sealed class ChannelWebhookSubscriptionEntity : EntityBase
{
    /// <summary>所属渠道配置 ID。</summary>
    public Guid ChannelConfigId { get; set; }

    /// <summary>订阅的渠道事件类型。</summary>
    public ChannelEventType EventType { get; set; }

    /// <summary>外部系统接收事件的 URL。</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>可选签名密钥，返回 API 时不透出。</summary>
    public string? Secret { get; set; }

    /// <summary>订阅是否启用。</summary>
    public bool Enabled { get; set; } = true;
}
