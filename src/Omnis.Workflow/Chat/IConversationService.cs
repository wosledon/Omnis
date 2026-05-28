using Omnis.Contracts.Chat;

namespace Omnis.Workflow.Chat;

/// <summary>
/// 对话引擎应用服务入口，负责会话生命周期、消息编排、反馈和人工转接。
/// </summary>
public interface IConversationService
{
    /// <summary>创建新会话，并固化用户身份、渠道和默认知识库范围。</summary>
    Task<ConversationCreatedResponse> CreateConversationAsync(
        CreateConversationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>按 ID 查询单个会话。</summary>
    Task<ConversationDto?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>按租户、可选工作空间和用户筛选最近会话。</summary>
    Task<IReadOnlyCollection<ConversationDto>> ListConversationsAsync(
        string tenantId,
        string? workspaceId,
        string? userId,
        CancellationToken cancellationToken = default);

    /// <summary>关闭会话，关闭后不再接受 AI 自动回复。</summary>
    Task<ConversationDto?> CloseConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>查询会话消息历史，按时间正序返回。</summary>
    Task<IReadOnlyCollection<ConversationMessageDto>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>写入用户消息，加载历史上下文调用 RAG，并保存 AI 回复。</summary>
    Task<SendConversationMessageResponse> SendMessageAsync(
        Guid conversationId,
        SendConversationMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>写入用户消息，并在 RAG/LLM 生成过程中实时返回 AI 文本增量，完成后保存 AI 回复。</summary>
    IAsyncEnumerable<ConversationStreamChunk> StreamMessageAsync(
        Guid conversationId,
        SendConversationMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>为某条消息追加用户反馈，并关联到该消息的 RAG 观测日志。</summary>
    Task<MessageFeedbackDto?> AddFeedbackAsync(
        Guid messageId,
        MessageFeedbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>创建人工转接记录，生成最近对话摘要，并将会话切换为转人工状态。</summary>
    Task<HumanHandoffDto?> CreateHandoffAsync(
        Guid conversationId,
        CreateHandoffRequest request,
        CancellationToken cancellationToken = default);
}
