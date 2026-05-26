using Omnis.Contracts.Channel;

namespace Omnis.Workflow.Channel;

/// <summary>
/// 对话渠道应用服务入口，负责渠道配置、Webhook 订阅和 Widget 启动配置。
/// </summary>
public interface IChannelService
{
    /// <summary>创建渠道配置，并保存非敏感设置、凭证和 Webhook 订阅。</summary>
    Task<ChannelConfigDto> CreateChannelAsync(
        CreateChannelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按 ID 查询单个渠道配置，返回结果不包含敏感凭证原文。</summary>
    Task<ChannelConfigDto?> GetChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken = default);

    /// <summary>按租户、工作空间、应用和渠道类型筛选渠道配置。</summary>
    Task<IReadOnlyCollection<ChannelConfigDto>> ListChannelsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        ChannelType? type,
        CancellationToken cancellationToken = default);

    /// <summary>更新渠道配置；请求中的凭证为空时保留原凭证。</summary>
    Task<ChannelConfigDto?> UpdateChannelAsync(
        Guid channelId,
        UpdateChannelConfigRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>禁用渠道配置，保留历史配置但阻止新的 Widget 启动或入口接入。</summary>
    Task<ChannelConfigDto?> DisableChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken = default);

    /// <summary>读取 Web Widget 公开启动配置，只返回前端安全可见字段。</summary>
    Task<ChannelWidgetBootstrapDto?> GetWidgetBootstrapAsync(
        Guid channelId,
        CancellationToken cancellationToken = default);
}
