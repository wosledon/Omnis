namespace Omnis.Contracts.Channel;

/// <summary>
/// 暴露给嵌入式 Web Widget 的品牌配置。
/// </summary>
public sealed record ChannelWidgetBrandingDto(
    /// <summary>Widget 标题，通常展示在聊天窗口头部。</summary>
    string Title,

    /// <summary>首次打开 Widget 时展示的欢迎语。</summary>
    string WelcomeMessage,

    /// <summary>主色值，建议使用十六进制颜色，例如 #2563eb。</summary>
    string PrimaryColor,

    /// <summary>品牌 Logo 地址，可为空。</summary>
    string? LogoUrl);

/// <summary>
/// 渠道管理 API 返回的 Webhook 订阅信息；签名密钥不会对外返回。
/// </summary>
public sealed record ChannelWebhookSubscriptionDto(
    /// <summary>Webhook 订阅 ID。</summary>
    Guid Id,

    /// <summary>订阅的事件类型。</summary>
    ChannelEventType EventType,

    /// <summary>外部系统接收事件的 URL。</summary>
    string Url,

    /// <summary>是否启用该订阅。</summary>
    bool Enabled);

/// <summary>
/// 返回给管理员后台的渠道配置。
/// </summary>
public sealed record ChannelConfigDto(
    /// <summary>渠道配置 ID。</summary>
    Guid Id,

    /// <summary>租户标识，是渠道数据隔离的第一层边界。</summary>
    string TenantId,

    /// <summary>工作空间标识，是业务线或部门级隔离边界。</summary>
    string WorkspaceId,

    /// <summary>可选应用标识，用于将渠道绑定到具体客服应用。</summary>
    string? ApplicationId,

    /// <summary>渠道类型。</summary>
    ChannelType Type,

    /// <summary>渠道展示名称，供管理后台识别。</summary>
    string Name,

    /// <summary>渠道生命周期状态。</summary>
    ChannelStatus Status,

    /// <summary>Web Widget 或独立页使用的品牌配置。</summary>
    ChannelWidgetBrandingDto? Widget,

    /// <summary>非敏感渠道配置，例如允许域名、欢迎语开关等。</summary>
    IReadOnlyDictionary<string, string> Settings,

    /// <summary>是否已经配置密钥或凭证；实际凭证不会返回。</summary>
    bool CredentialsConfigured,

    /// <summary>渠道绑定的 Webhook 订阅。</summary>
    IReadOnlyList<ChannelWebhookSubscriptionDto> Webhooks,

    /// <summary>创建时间。</summary>
    DateTimeOffset CreatedAt,

    /// <summary>最后更新时间。</summary>
    DateTimeOffset UpdatedAt);

/// <summary>
/// Widget 公开启动配置，只包含前端初始化必需信息，刻意排除凭证和 Webhook。
/// </summary>
public sealed record ChannelWidgetBootstrapDto(
    /// <summary>渠道配置 ID。</summary>
    Guid ChannelId,

    /// <summary>租户标识。</summary>
    string TenantId,

    /// <summary>工作空间标识。</summary>
    string WorkspaceId,

    /// <summary>可选应用标识。</summary>
    string? ApplicationId,

    /// <summary>Widget 品牌配置。</summary>
    ChannelWidgetBrandingDto Branding,

    /// <summary>Widget 可读取的非敏感配置。</summary>
    IReadOnlyDictionary<string, string> Settings);

/// <summary>
/// 创建或更新渠道时提交的 Webhook 订阅配置。
/// </summary>
public sealed record UpsertChannelWebhookSubscription(
    /// <summary>订阅的事件类型。</summary>
    ChannelEventType EventType,

    /// <summary>外部系统接收事件的 URL。</summary>
    string Url,

    /// <summary>用于签名校验的密钥，可为空；返回 DTO 中不会透出。</summary>
    string? Secret,

    /// <summary>是否启用该订阅。</summary>
    bool Enabled = true);

/// <summary>
/// 创建渠道配置请求。
/// </summary>
public sealed record CreateChannelConfigRequest(
    /// <summary>租户标识。</summary>
    string TenantId,

    /// <summary>工作空间标识。</summary>
    string WorkspaceId,

    /// <summary>可选应用标识。</summary>
    string? ApplicationId,

    /// <summary>渠道类型。</summary>
    ChannelType Type,

    /// <summary>渠道名称。</summary>
    string Name,

    /// <summary>初始状态，默认启用。</summary>
    ChannelStatus Status = ChannelStatus.Active,

    /// <summary>Widget 品牌配置。</summary>
    ChannelWidgetBrandingDto? Widget = null,

    /// <summary>非敏感配置。</summary>
    IReadOnlyDictionary<string, string>? Settings = null,

    /// <summary>敏感凭证，写入后不会在查询接口中返回。</summary>
    IReadOnlyDictionary<string, string>? Credentials = null,

    /// <summary>Webhook 订阅列表。</summary>
    IReadOnlyCollection<UpsertChannelWebhookSubscription>? Webhooks = null);

/// <summary>
/// 更新渠道配置请求。Credentials 为 null 时保留原有凭证。
/// </summary>
public sealed record UpdateChannelConfigRequest(
    /// <summary>渠道名称。</summary>
    string Name,

    /// <summary>渠道状态。</summary>
    ChannelStatus Status,

    /// <summary>Widget 品牌配置。</summary>
    ChannelWidgetBrandingDto? Widget = null,

    /// <summary>非敏感配置。</summary>
    IReadOnlyDictionary<string, string>? Settings = null,

    /// <summary>敏感凭证；传 null 表示不更新凭证。</summary>
    IReadOnlyDictionary<string, string>? Credentials = null,

    /// <summary>Webhook 订阅列表；传 null 表示不更新订阅。</summary>
    IReadOnlyCollection<UpsertChannelWebhookSubscription>? Webhooks = null);
