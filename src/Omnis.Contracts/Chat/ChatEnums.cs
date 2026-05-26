namespace Omnis.Contracts.Chat;

/// <summary>
/// 会话生命周期状态，决定对话引擎是否继续自动响应用户消息。
/// </summary>
public enum ConversationStatus
{
    /// <summary>会话进行中，AI 可以继续处理用户消息。</summary>
    Active = 0,

    /// <summary>会话已结束，仅保留历史查询和审计用途。</summary>
    Closed = 1,

    /// <summary>会话已进入人工接管队列，AI 默认不再直接外发回复。</summary>
    Handoff = 2
}

/// <summary>
/// 消息发送方角色，用于还原多轮上下文并区分用户、AI 与坐席消息。
/// </summary>
public enum MessageRole
{
    /// <summary>终端用户发送的消息。</summary>
    User = 0,

    /// <summary>AI 助手生成的消息。</summary>
    Assistant = 1,

    /// <summary>人工坐席发送的消息。</summary>
    Agent = 2,

    /// <summary>系统消息，通常用于内部提示或流程状态变更。</summary>
    System = 3
}

/// <summary>
/// 用户对 AI 回复的轻量反馈结果。
/// </summary>
public enum MessageFeedbackRating
{
    /// <summary>正向反馈，表示回答有帮助。</summary>
    Up = 0,

    /// <summary>负向反馈，表示回答无效、错误或不完整。</summary>
    Down = 1
}

/// <summary>
/// 人工转接触发来源，便于后续统计转人工原因。
/// </summary>
public enum HandoffTriggerType
{
    /// <summary>用户显式要求转人工。</summary>
    UserRequest = 0,

    /// <summary>回答置信度低于阈值后触发。</summary>
    LowConfidence = 1,

    /// <summary>用户负向反馈触发。</summary>
    NegativeFeedback = 2,

    /// <summary>系统规则或运营后台触发。</summary>
    System = 3
}

/// <summary>
/// 人工转接工单状态，MVP 阶段先覆盖单队列流转。
/// </summary>
public enum HandoffStatus
{
    /// <summary>已进入人工队列，等待坐席接入。</summary>
    Queued = 0,

    /// <summary>已分配给坐席。</summary>
    Assigned = 1,

    /// <summary>人工接管已处理完成。</summary>
    Resolved = 2,

    /// <summary>转接被取消或关闭。</summary>
    Cancelled = 3
}
