namespace Omnis.Retrieval.Rag;

/// <summary>
/// RAG 推理服务入口。
/// </summary>
public interface IRagService
{
    /// <summary>执行一次完整的 RAG 问答流程，返回答案、引用和调试信息。</summary>
    Task<RagAnswerResponse> AnswerAsync(RagAnswerRequest request, CancellationToken cancellationToken = default);

    /// <summary>执行一次流式 RAG 问答流程，检索完成后实时返回 LLM 生成增量，最后返回完整结果。</summary>
    IAsyncEnumerable<RagAnswerStreamChunk> AnswerStreamAsync(RagAnswerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 查询改写器，负责把用户问题和上下文整理成更适合检索的 query。
/// </summary>
public interface IRagQueryRewriter
{
    /// <summary>将原始问题改写成检索查询。</summary>
    Task<RewrittenQuery> RewriteAsync(RagAnswerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 混合检索器，负责向量检索与关键词检索的融合。
/// </summary>
public interface IHybridRetriever
{
    /// <summary>返回带有授权过滤后的检索候选结果。</summary>
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(HybridSearchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 检索重排序器，用于对候选片段做二次排序。
/// </summary>
public interface IRagReranker
{
    /// <summary>根据 query 对候选结果进行 rerank。</summary>
    Task<IReadOnlyList<RetrievalCandidate>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalCandidate> candidates,
        int topN,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Prompt 组装器，负责把问题、历史和检索上下文拼成发送给 LLM 的提示词。
/// </summary>
public interface IRagPromptBuilder
{
    /// <summary>构建最终 Prompt。</summary>
    string BuildPrompt(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations);
}

/// <summary>
/// LLM 生成器抽象。
/// </summary>
public interface IRagAnswerGenerator
{
    /// <summary>基于 Prompt 和检索上下文生成答案草稿。</summary>
    Task<RagAnswerDraft> GenerateAsync(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        string prompt,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        CancellationToken cancellationToken = default);

    /// <summary>基于 Prompt 和检索上下文流式生成答案草稿。</summary>
    IAsyncEnumerable<RagAnswerDraftStreamChunk> GenerateStreamAsync(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        string prompt,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG 观测持久化抽象。
/// </summary>
public interface IRagObservationSink
{
    /// <summary>保存一次 RAG 推理观测记录。</summary>
    Task SaveAsync(RagObservationRecord record, CancellationToken cancellationToken = default);
}
