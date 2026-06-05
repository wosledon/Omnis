using Microsoft.AspNetCore.Mvc;
using Omnis.Contracts.Channel;
using Omnis.Workflow.Channel;

namespace Omnis.Api.Endpoints;

/// <summary>
/// 对话渠道模块 HTTP 路由，覆盖渠道配置管理和 Web Widget 启动配置读取。
/// </summary>
public static class ChannelEndpoints
{
    /// <summary>
    /// 注册渠道模块 API，统一挂载在 /api 下以便后续 API Gateway 接入。
    /// </summary>
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Channels");

        // 创建渠道配置：写入租户/工作空间/应用绑定、渠道类型、Widget 配置、凭证和 Webhook 订阅。
        group.MapPost("/channel-configs", async (
            CreateChannelConfigRequest request,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await channels.CreateChannelAsync(request, cancellationToken);
                return Results.Created($"/api/channel-configs/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 查询渠道列表：tenantId 必填，workspaceId/applicationId/type 可选，满足管理后台筛选需求。
        group.MapGet("/channel-configs", async (
            [FromQuery] string tenantId,
            [FromQuery] string? workspaceId,
            [FromQuery] string? applicationId,
            [FromQuery] ChannelType? type,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await channels.ListChannelsAsync(tenantId, workspaceId, applicationId, type, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 查询单个渠道配置详情，服务层会隐藏凭证原文，仅返回是否已配置凭证。
        group.MapGet("/channel-configs/{channelId:guid}", async (
            Guid channelId,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            var result = await channels.GetChannelAsync(channelId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // 更新渠道配置：Credentials 为 null 时保留原凭证，Webhooks 为 null 时保留原订阅。
        group.MapPut("/channel-configs/{channelId:guid}", async (
            Guid channelId,
            UpdateChannelConfigRequest request,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await channels.UpdateChannelAsync(channelId, request, cancellationToken);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // 禁用渠道配置：保留配置记录和历史会话，但阻止新的渠道入口继续使用。
        group.MapPost("/channel-configs/{channelId:guid}/disable", async (
            Guid channelId,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            var result = await channels.DisableChannelAsync(channelId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Widget 公开启动配置：只返回前端可见的品牌和非敏感设置，不包含凭证和 Webhook。
        group.MapGet("/channel-configs/{channelId:guid}/widget/bootstrap", async (
            Guid channelId,
            IChannelService channels,
            CancellationToken cancellationToken) =>
        {
            var result = await channels.GetWidgetBootstrapAsync(channelId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return app;
    }
}
