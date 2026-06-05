namespace Omnis.Retrieval.Rag;

/// <summary>
/// RAG 问答请求。
/// </summary>
public sealed class RagAnswerRequest
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; init; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; init; } = string.Empty;
    /// <summary>应用标识，可用于观测和路由。</summary>
    public string? ApplicationId { get; init; }
    /// <summary>会话标识。</summary>
    public string? ConversationId { get; init; }
    /// <summary>消息标识。</summary>
    public string? MessageId { get; init; }
    /// <summary>提问用户标识。</summary>
    public string UserId { get; init; } = string.Empty;
    /// <summary>用户所属组。</summary>
    public string[] UserGroups { get; init; } = [];
    /// <summary>用户角色。</summary>
    public string[] UserRoles { get; init; } = [];
    /// <summary>限定可检索的知识库范围。</summary>
    public Guid[] KnowledgeBaseIds { get; init; } = [];
    /// <summary>用户原始问题。</summary>
    public string Question { get; init; } = string.Empty;
    /// <summary>最近的对话历史。</summary>
    public RagMessage[] ConversationHistory { get; init; } = [];
    /// <summary>RAG 过程参数。</summary>
    public RagOptions Options { get; init; } = new();
}

/// <summary>
/// RAG 处理参数。
/// </summary>
public sealed class RagOptions
{
    /// <summary>检索阶段取回的候选数量。</summary>
    public int RetrievalTopK { get; init; } = 8;
    /// <summary>最终进入 Prompt 的上下文条数。</summary>
    public int ContextTopN { get; init; } = 5;
    /// <summary>参与改写的最大历史轮数。</summary>
    public int MaxHistoryTurns { get; init; } = 10;
    /// <summary>向量检索权重。</summary>
    public double VectorWeight { get; init; } = 0.65;
    /// <summary>关键词检索权重。</summary>
    public double KeywordWeight { get; init; } = 0.35;
    /// <summary>知识边界兜底阈值。</summary>
    public double MinRelevanceScore { get; init; } = 0.5;
    /// <summary>触发转人工建议的置信度阈值。</summary>
    public double HandoffConfidenceThreshold { get; init; } = 0.6;
    /// <summary>是否启用重排序。</summary>
    public bool EnableRerank { get; init; } = true;
    /// <summary>是否严格只允许基于知识库回答。</summary>
    public bool StrictKnowledgeBoundary { get; init; } = true;
}

/// <summary>
/// 单轮对话消息。
/// </summary>
public sealed record RagMessage(string Role, string Content, DateTimeOffset? CreatedAt = null);

/// <summary>
/// RAG 问答返回结果。
/// </summary>
public sealed class RagAnswerResponse
{
    /// <summary>最终答案。</summary>
    public string Answer { get; init; } = string.Empty;
    /// <summary>原始问题。</summary>
    public string OriginalQuestion { get; init; } = string.Empty;
    /// <summary>改写后的检索问题。</summary>
    public string RewrittenQuery { get; init; } = string.Empty;
    /// <summary>置信度分数。</summary>
    public double ConfidenceScore { get; init; }
    /// <summary>是否建议转人工。</summary>
    public bool HandoffSuggested { get; init; }
    /// <summary>是否触发知识边界兜底。</summary>
    public bool KnowledgeBoundaryTriggered { get; init; }
    /// <summary>答案引用列表。</summary>
    public IReadOnlyList<RagCitation> Citations { get; init; } = [];
    /// <summary>检索到的候选分片。</summary>
    public IReadOnlyList<RagRetrievedChunk> RetrievedChunks { get; init; } = [];
    /// <summary>调试信息。</summary>
    public RagDebugTrace Debug { get; init; } = new();
}

/// <summary>
/// RAG 流式问答片段。Delta 用于前端实时展示，Completed 携带完整 RAG 结果。
/// </summary>
public sealed record RagAnswerStreamChunk(
    string ContentDelta,
    bool IsCompleted,
    RagAnswerResponse? Completed = null);

/// <summary>
/// 答案生成器的流式草稿片段。
/// </summary>
public sealed record RagAnswerDraftStreamChunk(
    string ContentDelta,
    bool IsCompleted,
    RagAnswerDraft? Completed = null);

/// <summary>
/// 结构化引用信息。
/// </summary>
public sealed record RagCitation(
    string Id,
    Guid DocumentId,
    Guid ChunkId,
    string Title,
    string Preview,
    string Url);

/// <summary>
/// 检索到的分片候选。
/// </summary>
public sealed class RagRetrievedChunk
{
    /// <summary>分片 ID。</summary>
    public Guid ChunkId { get; init; }
    /// <summary>文档 ID。</summary>
    public Guid DocumentId { get; init; }
    /// <summary>知识库 ID。</summary>
    public Guid KnowledgeBaseId { get; init; }
    /// <summary>文档标题。</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>分片序号。</summary>
    public int ChunkIndex { get; init; }
    /// <summary>内容预览。</summary>
    public string ContentPreview { get; init; } = string.Empty;
    /// <summary>向量分数。</summary>
    public double VectorScore { get; init; }
    /// <summary>关键词分数。</summary>
    public double KeywordScore { get; init; }
    /// <summary>融合分数。</summary>
    public double FusedScore { get; init; }
    /// <summary>重排序分数。</summary>
    public double? RerankScore { get; init; }
}

/// <summary>
/// RAG 调试链路信息。
/// </summary>
public sealed class RagDebugTrace
{
    /// <summary>最终 Prompt。</summary>
    public string Prompt { get; init; } = string.Empty;
    /// <summary>LLM 原始输出。</summary>
    public string LlmRawOutput { get; init; } = string.Empty;
    /// <summary>检索耗时。</summary>
    public long RetrievalDurationMs { get; init; }
    /// <summary>生成耗时。</summary>
    public long GenerationDurationMs { get; init; }
    /// <summary>总耗时。</summary>
    public long TotalDurationMs { get; init; }
}

/// <summary>
/// 检索权限上下文。
/// </summary>
public sealed record RagAccessContext(
    string UserId,
    IReadOnlyCollection<string> Groups,
    IReadOnlyCollection<string> Roles);

/// <summary>
/// 混合检索请求。
/// </summary>
public sealed class HybridSearchRequest
{
    /// <summary>租户标识。</summary>
    public string TenantId { get; init; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; init; } = string.Empty;
    /// <summary>知识库范围。</summary>
    public Guid[] KnowledgeBaseIds { get; init; } = [];
    /// <summary>检索 query。</summary>
    public string Query { get; init; } = string.Empty;
    /// <summary>访问权限上下文。</summary>
    public RagAccessContext Access { get; init; } = new(string.Empty, [], []);
    /// <summary>候选数量。</summary>
    public int TopK { get; init; } = 8;
    /// <summary>向量权重。</summary>
    public double VectorWeight { get; init; } = 0.65;
    /// <summary>关键词权重。</summary>
    public double KeywordWeight { get; init; } = 0.35;
}

/// <summary>
/// 检索候选记录。
/// </summary>
public sealed record RetrievalCandidate
{
    /// <summary>分片 ID。</summary>
    public Guid ChunkId { get; init; }
    /// <summary>文档 ID。</summary>
    public Guid DocumentId { get; init; }
    /// <summary>知识库 ID。</summary>
    public Guid KnowledgeBaseId { get; init; }
    /// <summary>文档标题。</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>分片序号。</summary>
    public int ChunkIndex { get; init; }
    /// <summary>分片内容。</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>向量分数。</summary>
    public double VectorScore { get; init; }
    /// <summary>关键词分数。</summary>
    public double KeywordScore { get; init; }
    /// <summary>融合分数。</summary>
    public double FusedScore { get; init; }
    /// <summary>重排序分数。</summary>
    public double? RerankScore { get; init; }
}

/// <summary>
/// 改写后的查询。
/// </summary>
public sealed record RewrittenQuery(string OriginalQuestion, string Query);

/// <summary>
/// 答案草稿。
/// </summary>
public sealed class RagAnswerDraft
{
    /// <summary>生成的答案。</summary>
    public string Answer { get; init; } = string.Empty;
    /// <summary>LLM 原始输出。</summary>
    public string RawOutput { get; init; } = string.Empty;
    /// <summary>答案完整性分数。</summary>
    public double CompletenessScore { get; init; } = 0.8;
    /// <summary>模型自评分数。</summary>
    public double SelfScore { get; init; } = 0.8;
    /// <summary>引用来源 ID 集合。</summary>
    public IReadOnlyCollection<string> CitationIds { get; init; } = [];
}

/// <summary>
/// RAG 观测记录。
/// </summary>
public sealed class RagObservationRecord
{
    /// <summary>记录主键。</summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>租户标识。</summary>
    public string TenantId { get; init; } = string.Empty;
    /// <summary>工作空间标识。</summary>
    public string WorkspaceId { get; init; } = string.Empty;
    /// <summary>应用 ID。</summary>
    public string? ApplicationId { get; init; }
    /// <summary>会话 ID。</summary>
    public string? ConversationId { get; init; }
    /// <summary>消息 ID。</summary>
    public string? MessageId { get; init; }
    /// <summary>用户 ID。</summary>
    public string UserId { get; init; } = string.Empty;
    /// <summary>用户问题。</summary>
    public string UserQuestion { get; init; } = string.Empty;
    /// <summary>改写后的 query。</summary>
    public string RewrittenQuery { get; init; } = string.Empty;
    /// <summary>检索到的分片。</summary>
    public IReadOnlyList<RagRetrievedChunk> RetrievedChunks { get; init; } = [];
    /// <summary>最终 Prompt。</summary>
    public string FinalPrompt { get; init; } = string.Empty;
    /// <summary>LLM 原始输出。</summary>
    public string LlmRawOutput { get; init; } = string.Empty;
    /// <summary>最终答案。</summary>
    public string FinalAnswer { get; init; } = string.Empty;
    /// <summary>置信度。</summary>
    public double ConfidenceScore { get; init; }
    /// <summary>引用来源 ID 数组。</summary>
    public string[] CitationSourceIds { get; init; } = [];
    /// <summary>是否存在幻觉或引用异常。</summary>
    public bool HasHallucination { get; init; }
    /// <summary>检索耗时。</summary>
    public long RetrievalDurationMs { get; init; }
    /// <summary>生成耗时。</summary>
    public long GenerationDurationMs { get; init; }
    /// <summary>总耗时。</summary>
    public long TotalDurationMs { get; init; }
    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
