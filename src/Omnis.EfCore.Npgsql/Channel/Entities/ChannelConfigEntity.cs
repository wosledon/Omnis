using Omnis.Contracts.Channel;
using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Npgsql.Channel.Entities;

/// <summary>
/// 渠道配置持久化实体，对应 channel_configs 表。
/// </summary>
public sealed class ChannelConfigEntity : EntityBase
{
    /// <summary>租户标识，渠道配置的数据隔离边界。</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>工作空间标识，业务线或部门级隔离边界。</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>可选应用标识，用于绑定具体客服应用。</summary>
    public string? ApplicationId { get; set; }

    /// <summary>渠道类型。</summary>
    public ChannelType Type { get; set; }

    /// <summary>渠道展示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>渠道生命周期状态。</summary>
    public ChannelStatus Status { get; set; } = ChannelStatus.Active;

    /// <summary>Widget 品牌和启动配置 JSON。</summary>
    public string WidgetJson { get; set; } = "{}";

    /// <summary>非敏感渠道设置 JSON。</summary>
    public string SettingsJson { get; set; } = "{}";

    /// <summary>敏感凭证 JSON；服务层不会将原文返回给 API 调用方。</summary>
    public string CredentialsJson { get; set; } = "{}";
}
