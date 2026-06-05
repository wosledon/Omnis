namespace Omnis.Contracts.Channel;

/// <summary>
/// 对话入口渠道类型，覆盖 MVP 的 Web Widget / REST API，并预留后续 IM 与实时通道。
/// </summary>
public enum ChannelType
{
    /// <summary>嵌入业务系统页面的聊天组件。</summary>
    WebWidget = 0,

    /// <summary>面向第三方系统集成的标准 HTTP API。</summary>
    RestApi = 1,

    /// <summary>可独立访问的客服对话页面。</summary>
    StandalonePage = 2,

    /// <summary>通过 Webhook 推送或接收外部系统事件。</summary>
    Webhook = 3,

    /// <summary>微信公众号消息入口。</summary>
    WeChatOfficialAccount = 10,

    /// <summary>企业微信应用消息入口。</summary>
    WeCom = 11,

    /// <summary>钉钉机器人或应用消息入口。</summary>
    DingTalk = 12,

    /// <summary>飞书机器人或应用消息入口。</summary>
    Feishu = 13,

    /// <summary>实时双向通信通道。</summary>
    WebSocket = 20
}

/// <summary>
/// 渠道配置生命周期状态，用于控制渠道是否可被终端用户访问。
/// </summary>
public enum ChannelStatus
{
    /// <summary>草稿状态，配置尚未对外生效。</summary>
    Draft = 0,

    /// <summary>启用状态，可用于创建会话或加载 Widget。</summary>
    Active = 1,

    /// <summary>已禁用，保留配置但拒绝新的入口请求。</summary>
    Disabled = 2,

    /// <summary>已归档，通常仅用于历史审计。</summary>
    Archived = 3
}

/// <summary>
/// 可通过 Webhook 推送给外部系统的渠道事件类型。
/// </summary>
public enum ChannelEventType
{
    /// <summary>会话创建事件。</summary>
    ConversationCreated = 0,

    /// <summary>消息创建事件。</summary>
    MessageCreated = 1,

    /// <summary>用户反馈提交事件。</summary>
    FeedbackSubmitted = 2,

    /// <summary>人工转接创建事件。</summary>
    HandoffCreated = 3
}
