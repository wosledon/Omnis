using Omnis.Contracts.Knowledge;

namespace Omnis.Contracts.Chat;

/// <summary>
/// 会话中的终端用户身份快照，创建会话时固化到会话记录中。
/// </summary>
public sealed record ConversationUser(
    /// <summary>业务侧用户标识。</summary>
    string Id,
    /// <summary>用户展示名，可为空。</summary>
    string? Name,
    /// <summary>用户所属用户组，用于 RAG 文档 ACL 过滤。</summary>
    string[] Groups,
    /// <summary>用户拥有的角色，用于 RAG 文档 ACL 过滤。</summary>
    string[] Roles);

/// <summary>
/// 创建会话请求，明确租户、工作空间、渠道和默认知识库范围。
/// </summary>
public sealed record CreateConversationRequest(
    /// <summary>租户标识，是业务数据隔离的第一层边界。</summary>
    string TenantId,
    /// <summary>工作空间标识，是业务线或部门级隔离边界。</summary>
    string WorkspaceId,
    /// <summary>应用标识，可用于绑定模型、RAG 参数和渠道配置。</summary>
    string? ApplicationId,
    /// <summary>会话来源渠道，例如 web_widget、rest_api。</summary>
    string Channel,
    /// <summary>发起会话的用户身份。</summary>
    ConversationUser User,
    /// <summary>默认可检索的知识库范围。</summary>
    Guid[] KnowledgeBaseIds);

/// <summary>
/// 会话返回模型，供管理后台、Widget 和第三方集成查询。
/// </summary>
public sealed record ConversationDto(
    /// <summary>会话主键。</summary>
    Guid Id,
    /// <summary>租户标识。</summary>
    string TenantId,
    /// <summary>工作空间标识。</summary>
    string WorkspaceId,
    /// <summary>应用标识。</summary>
    string? ApplicationId,
    /// <summary>用户标识。</summary>
    string UserId,
    /// <summary>用户展示名。</summary>
    string? UserName,
    /// <summary>用户组快照。</summary>
    string[] UserGroups,
    /// <summary>用户角色快照。</summary>
    string[] UserRoles,
    /// <summary>会话渠道。</summary>
    string Channel,
    /// <summary>当前会话状态。</summary>
    ConversationStatus Status,
    /// <summary>默认知识库范围。</summary>
    Guid[] KnowledgeBaseIds,
    /// <summary>创建时间。</summary>
    DateTimeOffset CreatedAt,
    /// <summary>关闭时间，未关闭时为空。</summary>
    DateTimeOffset? ClosedAt);

/// <summary>
/// 创建会话响应，只返回前端继续对话所需的最小信息。
/// </summary>
public sealed record ConversationCreatedResponse(
    /// <summary>新建会话 ID。</summary>
    Guid ConversationId,
    /// <summary>初始会话状态。</summary>
    ConversationStatus Status);

/// <summary>
/// 用户向会话发送消息的请求。
/// </summary>
public sealed record SendConversationMessageRequest(
    /// <summary>用户输入正文。</summary>
    string Content,
    /// <summary>是否以 SSE 形式返回。当前实现为兼容包装，待 RAG/LLM 层提供原生流式后可替换。</summary>
    bool Stream = false,
    /// <summary>本轮覆盖使用的知识库范围；为空时使用会话默认范围。</summary>
    Guid[]? KnowledgeBaseIds = null,
    /// <summary>本轮 RAG 参数覆盖。</summary>
    ChatRagOptions? Options = null);

/// <summary>
/// 对话层暴露的 RAG 参数子集，避免 API 调用方直接依赖检索层内部模型。
/// </summary>
public sealed record ChatRagOptions(
    /// <summary>检索候选数量。</summary>
    int RetrievalTopK = 8,
    /// <summary>进入最终 Prompt 的上下文数量。</summary>
    int ContextTopN = 5,
    /// <summary>参与多轮上下文的最大轮数。</summary>
    int MaxHistoryTurns = 10,
    /// <summary>知识边界兜底阈值，低于该分数时不编造答案。</summary>
    double MinRelevanceScore = 0.5,
    /// <summary>向量检索权重。</summary>
    double VectorWeight = 0.45,
    /// <summary>关键词检索权重。</summary>
    double KeywordWeight = 0.55,
    /// <summary>建议转人工的置信度阈值。</summary>
    double HandoffConfidenceThreshold = 0.6,
    /// <summary>是否严格限制答案只能来自知识库。</summary>
    bool StrictKnowledgeBoundary = true);

/// <summary>
/// 会话消息返回模型。
/// </summary>
public sealed record ConversationMessageDto(
    /// <summary>消息主键。</summary>
    Guid Id,
    /// <summary>所属会话 ID。</summary>
    Guid ConversationId,
    /// <summary>消息角色。</summary>
    MessageRole Role,
    /// <summary>消息正文。</summary>
    string Content,
    /// <summary>AI 消息携带的引用来源，用户消息通常为空。</summary>
    IReadOnlyList<ChatCitationDto> Citations,
    /// <summary>AI 回复置信度，非 AI 消息通常为空。</summary>
    double? ConfidenceScore,
    /// <summary>消息创建时间。</summary>
    DateTimeOffset CreatedAt);

/// <summary>
/// 对话层引用来源模型，对齐 Widget 和管理后台展示需要。
/// </summary>
public sealed record ChatCitationDto(
    /// <summary>本次回答内的引用编号，例如 source-1。</summary>
    string Id,
    /// <summary>来源文档 ID。</summary>
    Guid DocumentId,
    /// <summary>来源分片 ID。</summary>
    Guid ChunkId,
    /// <summary>来源标题。</summary>
    string Title,
    /// <summary>来源内容预览。</summary>
    string Preview,
    /// <summary>跳转或定位 URL。</summary>
    string Url);

/// <summary>
/// 发送消息后的完整响应。
/// </summary>
public sealed record SendConversationMessageResponse(
    /// <summary>本轮用户消息 ID。</summary>
    Guid UserMessageId,
    /// <summary>本轮 AI 回复消息 ID。</summary>
    Guid AssistantMessageId,
    /// <summary>AI 最终答案。</summary>
    string Answer,
    /// <summary>回答置信度。</summary>
    double ConfidenceScore,
    /// <summary>回答引用来源。</summary>
    IReadOnlyList<ChatCitationDto> Citations,
    /// <summary>是否建议转人工。</summary>
    bool HandoffSuggested,
    /// <summary>是否触发知识边界兜底。</summary>
    bool KnowledgeBoundaryTriggered);

/// <summary>
/// 用户反馈提交请求。
/// </summary>
public sealed record MessageFeedbackRequest(
    /// <summary>点赞或点踩。</summary>
    MessageFeedbackRating Rating,
    /// <summary>反馈原因，可由前端传入标准原因码或自由文本。</summary>
    string? Reason,
    /// <summary>反馈用户 ID，匿名场景可为空。</summary>
    string? UserId);

/// <summary>
/// 用户反馈返回模型。
/// </summary>
public sealed record MessageFeedbackDto(
    /// <summary>反馈记录 ID。</summary>
    Guid Id,
    /// <summary>被评价的消息 ID。</summary>
    Guid MessageId,
    /// <summary>反馈用户 ID。</summary>
    string UserId,
    /// <summary>反馈结果。</summary>
    MessageFeedbackRating Rating,
    /// <summary>反馈原因。</summary>
    string? Reason,
    /// <summary>反馈创建时间。</summary>
    DateTimeOffset CreatedAt);

/// <summary>
/// 创建人工转接请求。
/// </summary>
public sealed record CreateHandoffRequest(
    /// <summary>转人工触发类型。</summary>
    HandoffTriggerType TriggerType,
    /// <summary>触发转接时关联的上一条 AI 消息。</summary>
    Guid? LastAiMessageId);

/// <summary>
/// 人工转接摘要，帮助坐席快速理解上下文。
/// </summary>
public sealed record HandoffSummaryDto(
    /// <summary>用户核心意图。</summary>
    string Intent,
    /// <summary>已从对话中确认的信息。</summary>
    string[] ConfirmedFacts,
    /// <summary>仍未解决的问题。</summary>
    string[] UnresolvedIssues,
    /// <summary>建议坐席接入后的首句回复。</summary>
    string SuggestedReply);

/// <summary>
/// 人工转接记录返回模型。
/// </summary>
public sealed record HumanHandoffDto(
    /// <summary>转接记录 ID。</summary>
    Guid Id,
    /// <summary>所属会话 ID。</summary>
    Guid ConversationId,
    /// <summary>触发类型。</summary>
    HandoffTriggerType TriggerType,
    /// <summary>转接摘要。</summary>
    HandoffSummaryDto Summary,
    /// <summary>关联的上一条 AI 消息。</summary>
    Guid? LastAiMessageId,
    /// <summary>转接状态。</summary>
    HandoffStatus Status,
    /// <summary>已分配坐席 ID，未分配时为空。</summary>
    string? AssignedAgentId,
    /// <summary>创建时间。</summary>
    DateTimeOffset CreatedAt);
