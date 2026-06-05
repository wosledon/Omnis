using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Omnis.Contracts.Channel;
using Omnis.EfCore.Npgsql.Channel.Entities;
using Omnis.Workflow.Channel;

namespace Omnis.EfCore.Npgsql.Channel.Services;

/// <summary>
/// PostgreSQL 版本渠道服务，负责渠道配置、Widget 启动配置和 Webhook 订阅的持久化。
/// </summary>
internal sealed class PostgresChannelService(
    OmnisNpgsqlDbContext dbContext
) : IChannelService
{
    // 渠道扩展配置统一以 jsonb 保存，序列化风格与 API 默认 camelCase 保持一致。
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 创建渠道配置。凭证只写入数据库，返回 DTO 仅暴露 CredentialsConfigured 标记。
    /// </summary>
    public async Task<ChannelConfigDto> CreateChannelAsync(
        CreateChannelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var now = DateTime.UtcNow;
        var entity = new ChannelConfigEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            WorkspaceId = request.WorkspaceId.Trim(),
            ApplicationId = NormalizeOptional(request.ApplicationId),
            Type = request.Type,
            Name = request.Name.Trim(),
            Status = request.Status,
            WidgetJson = ToJson(NormalizeWidget(request.Type, request.Widget)),
            SettingsJson = ToJson(NormalizeDictionary(request.Settings)),
            CredentialsJson = ToJson(NormalizeDictionary(request.Credentials)),
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ChannelConfigs.Add(entity);
        await ReplaceWebhookSubscriptionsAsync(entity.Id, request.Webhooks, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var webhooks = await LoadWebhookDtosAsync(entity.Id, cancellationToken);
        return ToChannelDto(entity, webhooks);
    }

    /// <summary>
    /// 按 ID 查询渠道配置，并附带该渠道下的 Webhook 订阅列表。
    /// </summary>
    public async Task<ChannelConfigDto?> GetChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ChannelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var webhooks = await LoadWebhookDtosAsync(channelId, cancellationToken);
        return ToChannelDto(entity, webhooks);
    }

    /// <summary>
    /// 查询渠道列表，默认限制最多返回 200 条，避免管理后台一次性拉取过多配置。
    /// </summary>
    public async Task<IReadOnlyCollection<ChannelConfigDto>> ListChannelsAsync(
        string tenantId,
        string? workspaceId,
        string? applicationId,
        ChannelType? type,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var normalizedWorkspaceId = NormalizeOptional(workspaceId);
        var normalizedApplicationId = NormalizeOptional(applicationId);
        var query = dbContext.ChannelConfigs
            .AsNoTracking()
            .Where(channel => channel.TenantId == tenantId.Trim());

        if (normalizedWorkspaceId is not null)
        {
            query = query.Where(channel => channel.WorkspaceId == normalizedWorkspaceId);
        }

        if (normalizedApplicationId is not null)
        {
            query = query.Where(channel => channel.ApplicationId == normalizedApplicationId);
        }

        if (type.HasValue)
        {
            query = query.Where(channel => channel.Type == type.Value);
        }

        var entities = await query
            .OrderByDescending(channel => channel.UpdatedAt ?? channel.CreatedAt)
            .Take(200)
            .ToArrayAsync(cancellationToken);

        var channelIds = entities.Select(channel => channel.Id).ToArray();
        var webhooks = await dbContext.ChannelWebhookSubscriptions
            .AsNoTracking()
            .Where(webhook => channelIds.Contains(webhook.ChannelConfigId))
            .OrderBy(webhook => webhook.EventType)
            .ThenBy(webhook => webhook.Url)
            .ToArrayAsync(cancellationToken);

        var webhooksByChannel = webhooks
            .GroupBy(webhook => webhook.ChannelConfigId)
            .ToDictionary(group => group.Key, group => group.Select(ToWebhookDto).ToArray() as IReadOnlyList<ChannelWebhookSubscriptionDto>);

        return entities
            .Select(entity => ToChannelDto(entity, webhooksByChannel.GetValueOrDefault(entity.Id) ?? []))
            .ToArray();
    }

    /// <summary>
    /// 更新渠道配置。凭证和 Webhook 都采用显式传入才替换的策略，避免局部编辑误清空敏感配置。
    /// </summary>
    public async Task<ChannelConfigDto?> UpdateChannelAsync(
        Guid channelId,
        UpdateChannelConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var entity = await dbContext.ChannelConfigs
            .FirstOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = request.Name.Trim();
        entity.Status = request.Status;
        entity.WidgetJson = ToJson(NormalizeWidget(entity.Type, request.Widget));
        entity.SettingsJson = ToJson(NormalizeDictionary(request.Settings));
        if (request.Credentials is not null)
        {
            entity.CredentialsJson = ToJson(NormalizeDictionary(request.Credentials));
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await ReplaceWebhookSubscriptionsAsync(entity.Id, request.Webhooks, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var webhooks = await LoadWebhookDtosAsync(entity.Id, cancellationToken);
        return ToChannelDto(entity, webhooks);
    }

    /// <summary>
    /// 禁用渠道配置，保留历史记录供审计和后台查看。
    /// </summary>
    public async Task<ChannelConfigDto?> DisableChannelAsync(
        Guid channelId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ChannelConfigs
            .FirstOrDefaultAsync(channel => channel.Id == channelId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Status = ChannelStatus.Disabled;
        entity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var webhooks = await LoadWebhookDtosAsync(entity.Id, cancellationToken);
        return ToChannelDto(entity, webhooks);
    }

    /// <summary>
    /// 读取 Widget 启动配置。只有启用状态的 WebWidget 渠道才允许公开读取。
    /// </summary>
    public async Task<ChannelWidgetBootstrapDto?> GetWidgetBootstrapAsync(
        Guid channelId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ChannelConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(channel =>
                channel.Id == channelId &&
                channel.Type == ChannelType.WebWidget &&
                channel.Status == ChannelStatus.Active,
                cancellationToken);
        if (entity is null)
        {
            return null;
        }

        return new ChannelWidgetBootstrapDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            FromJson<ChannelWidgetBrandingDto>(entity.WidgetJson) ?? DefaultWidget(),
            FromJson<Dictionary<string, string>>(entity.SettingsJson) ?? new Dictionary<string, string>());
    }

    /// <summary>
    /// 替换渠道 Webhook 订阅；当调用方传 null 时表示不修改现有订阅。
    /// </summary>
    async Task ReplaceWebhookSubscriptionsAsync(
        Guid channelId,
        IReadOnlyCollection<UpsertChannelWebhookSubscription>? subscriptions,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        if (subscriptions is null)
        {
            return;
        }

        var existing = await dbContext.ChannelWebhookSubscriptions
            .Where(webhook => webhook.ChannelConfigId == channelId)
            .ToArrayAsync(cancellationToken);

        dbContext.ChannelWebhookSubscriptions.RemoveRange(existing);

        var now = DateTime.UtcNow;
        var entities = subscriptions
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.Url))
            .Select(subscription => NormalizeWebhook(subscription, channelId, actorId, now))
            .ToArray();

        dbContext.ChannelWebhookSubscriptions.AddRange(entities);
    }

    /// <summary>
    /// 校验并规范化单条 Webhook 订阅，确保只接受绝对 HTTP/HTTPS 地址。
    /// </summary>
    static ChannelWebhookSubscriptionEntity NormalizeWebhook(
        UpsertChannelWebhookSubscription subscription,
        Guid channelId,
        Guid? actorId,
        DateTime now)
    {
        var url = subscription.Url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Webhook URL must be an absolute HTTP or HTTPS URL.");
        }

        return new ChannelWebhookSubscriptionEntity
        {
            Id = Guid.NewGuid(),
            ChannelConfigId = channelId,
            EventType = subscription.EventType,
            Url = url,
            Secret = NormalizeOptional(subscription.Secret),
            Enabled = subscription.Enabled,
            CreatedBy = actorId,
            CreatedAt = now,
            UpdatedBy = actorId,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// 加载渠道下的 Webhook 订阅，并转换为不会泄露 secret 的 DTO。
    /// </summary>
    async Task<IReadOnlyList<ChannelWebhookSubscriptionDto>> LoadWebhookDtosAsync(
        Guid channelId,
        CancellationToken cancellationToken)
    {
        var entities = await dbContext.ChannelWebhookSubscriptions
            .AsNoTracking()
            .Where(webhook => webhook.ChannelConfigId == channelId)
            .OrderBy(webhook => webhook.EventType)
            .ThenBy(webhook => webhook.Url)
            .ToArrayAsync(cancellationToken);

        return entities.Select(ToWebhookDto).ToArray();
    }

    /// <summary>
    /// 将渠道实体转换为 API DTO，隐藏 CredentialsJson 原文。
    /// </summary>
    static ChannelConfigDto ToChannelDto(
        ChannelConfigEntity entity,
        IReadOnlyList<ChannelWebhookSubscriptionDto> webhooks)
    {
        var settings = FromJson<Dictionary<string, string>>(entity.SettingsJson) ?? new Dictionary<string, string>();
        var credentials = FromJson<Dictionary<string, string>>(entity.CredentialsJson) ?? new Dictionary<string, string>();

        return new ChannelConfigDto(
            entity.Id,
            entity.TenantId,
            entity.WorkspaceId,
            entity.ApplicationId,
            entity.Type,
            entity.Name,
            entity.Status,
            FromJson<ChannelWidgetBrandingDto>(entity.WidgetJson),
            settings,
            credentials.Count > 0,
            webhooks,
            ToDateTimeOffset(entity.CreatedAt),
            ToDateTimeOffset(entity.UpdatedAt ?? entity.CreatedAt));
    }

    /// <summary>
    /// 将 Webhook 实体转换为 API DTO，避免泄露签名密钥。
    /// </summary>
    static ChannelWebhookSubscriptionDto ToWebhookDto(ChannelWebhookSubscriptionEntity entity)
    {
        return new ChannelWebhookSubscriptionDto(
            entity.Id,
            entity.EventType,
            entity.Url,
            entity.Enabled);
    }

    /// <summary>
    /// 规范化 Widget 品牌配置，Web Widget 与独立页缺省时使用默认品牌值。
    /// </summary>
    static ChannelWidgetBrandingDto? NormalizeWidget(ChannelType type, ChannelWidgetBrandingDto? widget)
    {
        if (type != ChannelType.WebWidget && type != ChannelType.StandalonePage)
        {
            return widget;
        }

        if (widget is null)
        {
            return DefaultWidget();
        }

        var title = NormalizeOptional(widget.Title) ?? DefaultWidget().Title;
        var welcomeMessage = NormalizeOptional(widget.WelcomeMessage) ?? DefaultWidget().WelcomeMessage;
        var primaryColor = NormalizeOptional(widget.PrimaryColor) ?? DefaultWidget().PrimaryColor;

        return widget with
        {
            Title = title,
            WelcomeMessage = welcomeMessage,
            PrimaryColor = primaryColor,
            LogoUrl = NormalizeOptional(widget.LogoUrl)
        };
    }

    /// <summary>
    /// MVP 阶段的默认 Widget 品牌配置。
    /// </summary>
    static ChannelWidgetBrandingDto DefaultWidget()
    {
        return new ChannelWidgetBrandingDto(
            "Omnis Assistant",
            "Hi, how can I help?",
            "#2563eb",
            null);
    }

    /// <summary>
    /// 规范化配置字典，过滤空键空值并按大小写不敏感方式处理重复键。
    /// </summary>
    static Dictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? values)
    {
        return values is null
            ? new Dictionary<string, string>()
            : values
                .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key.Trim(), item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 规范化可选字符串，空白值统一转为 null。
    /// </summary>
    static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// 使用统一 JSON 配置序列化 jsonb 字段。
    /// </summary>
    static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    /// <summary>
    /// 使用统一 JSON 配置反序列化 jsonb 字段。
    /// </summary>
    static T? FromJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// 将 EF 实体中的 UTC DateTime 转换为 API 使用的 DateTimeOffset。
    /// </summary>
    static DateTimeOffset ToDateTimeOffset(DateTime? value)
    {
        var dateTime = value ?? DateTime.UtcNow;
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        }

        return new DateTimeOffset(dateTime.ToUniversalTime());
    }
}
