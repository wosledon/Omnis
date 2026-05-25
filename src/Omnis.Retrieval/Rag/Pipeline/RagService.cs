using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Omnis.Retrieval.Rag;

/// <summary>
/// RAG 主流程实现，串联改写、检索、重排、生成和观测落库。
/// </summary>
internal sealed class RagService(
    IRagQueryRewriter queryRewriter,
    IHybridRetriever retriever,
    IRagReranker reranker,
    IRagPromptBuilder promptBuilder,
    IRagAnswerGenerator answerGenerator,
    IRagObservationSink observationSink,
    ILogger<RagService> logger) : IRagService
{
    /// <summary>
    /// 执行一次完整的 RAG 问答，返回答案、引用、检索结果和调试信息。
    /// </summary>
    public async Task<RagAnswerResponse> AnswerAsync(RagAnswerRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);

        var total = Stopwatch.StartNew();
        var rewrittenQuery = await queryRewriter.RewriteAsync(request, cancellationToken);

        var retrievalWatch = Stopwatch.StartNew();
        var candidates = await retriever.SearchAsync(new HybridSearchRequest
        {
            TenantId = request.TenantId.Trim(),
            WorkspaceId = request.WorkspaceId.Trim(),
            KnowledgeBaseIds = request.KnowledgeBaseIds,
            Query = rewrittenQuery.Query,
            TopK = Math.Max(1, request.Options.RetrievalTopK),
            VectorWeight = ClampWeight(request.Options.VectorWeight),
            KeywordWeight = ClampWeight(request.Options.KeywordWeight),
            Access = new RagAccessContext(
                request.UserId.Trim(),
                NormalizePrincipals(request.UserGroups),
                NormalizePrincipals(request.UserRoles))
        }, cancellationToken);
        retrievalWatch.Stop();

        var topN = Math.Max(1, request.Options.ContextTopN);
        var context = request.Options.EnableRerank
            ? await reranker.RerankAsync(rewrittenQuery.Query, candidates, topN, cancellationToken)
            : candidates.Take(topN).ToArray();

        var citations = CreateCitations(context);
        var topScore = context.Count == 0 ? 0 : context.Max(candidate => candidate.RerankScore ?? candidate.FusedScore);
        var boundaryTriggered = request.Options.StrictKnowledgeBoundary && topScore < request.Options.MinRelevanceScore;
        var prompt = promptBuilder.BuildPrompt(request, rewrittenQuery, context, citations);

        var generationWatch = Stopwatch.StartNew();
        var draft = boundaryTriggered
            ? CreateBoundaryDraft(rewrittenQuery.Query)
            : await answerGenerator.GenerateAsync(request, rewrittenQuery, prompt, context, citations, cancellationToken);
        generationWatch.Stop();
        total.Stop();

        var validCitationIds = citations.Select(citation => citation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCitationIds = draft.CitationIds.Where(validCitationIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hasHallucination = draft.CitationIds.Any(id => !validCitationIds.Contains(id));
        var usedCitations = citations.Where(citation => usedCitationIds.Contains(citation.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var retrieved = context.Select(ToRetrievedChunk).ToArray();
        var confidence = boundaryTriggered
            ? 0
            : CalculateConfidence(context, draft);

        var response = new RagAnswerResponse
        {
            Answer = draft.Answer,
            OriginalQuestion = rewrittenQuery.OriginalQuestion,
            RewrittenQuery = rewrittenQuery.Query,
            ConfidenceScore = confidence,
            HandoffSuggested = confidence < request.Options.HandoffConfidenceThreshold,
            KnowledgeBoundaryTriggered = boundaryTriggered,
            Citations = usedCitations,
            RetrievedChunks = retrieved,
            Debug = new RagDebugTrace
            {
                Prompt = prompt,
                LlmRawOutput = draft.RawOutput,
                RetrievalDurationMs = retrievalWatch.ElapsedMilliseconds,
                GenerationDurationMs = generationWatch.ElapsedMilliseconds,
                TotalDurationMs = total.ElapsedMilliseconds
            }
        };

        await SaveObservationAsync(request, response, hasHallucination, cancellationToken);
        return response;
    }

    async Task SaveObservationAsync(
        RagAnswerRequest request,
        RagAnswerResponse response,
        bool hasHallucination,
        CancellationToken cancellationToken)
    {
        try
        {
            await observationSink.SaveAsync(new RagObservationRecord
            {
                TenantId = request.TenantId,
                WorkspaceId = request.WorkspaceId,
                ApplicationId = request.ApplicationId,
                ConversationId = request.ConversationId,
                MessageId = request.MessageId,
                UserId = request.UserId,
                UserQuestion = response.OriginalQuestion,
                RewrittenQuery = response.RewrittenQuery,
                RetrievedChunks = response.RetrievedChunks,
                FinalPrompt = response.Debug.Prompt,
                LlmRawOutput = response.Debug.LlmRawOutput,
                FinalAnswer = response.Answer,
                ConfidenceScore = response.ConfidenceScore,
                CitationSourceIds = response.Citations.Select(citation => citation.Id).ToArray(),
                HasHallucination = hasHallucination,
                RetrievalDurationMs = response.Debug.RetrievalDurationMs,
                GenerationDurationMs = response.Debug.GenerationDurationMs,
                TotalDurationMs = response.Debug.TotalDurationMs
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save RAG observation for tenant {TenantId}.", request.TenantId);
        }
    }

    static void Validate(RagAnswerRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);
    }

    static RagAnswerDraft CreateBoundaryDraft(string query)
    {
        var answer = $"抱歉，我没有在知识库中找到与“{query}”足够相关的答案。建议转人工处理，或补充更具体的问题。";
        return new RagAnswerDraft
        {
            Answer = answer,
            RawOutput = answer,
            CompletenessScore = 0,
            SelfScore = 0,
            CitationIds = []
        };
    }

    static IReadOnlyList<RagCitation> CreateCitations(IReadOnlyList<RetrievalCandidate> context)
    {
        return context.Select((candidate, index) => new RagCitation(
            $"source-{index + 1}",
            candidate.DocumentId,
            candidate.ChunkId,
            candidate.Title,
            Preview(candidate.Content, 180),
            $"/api/documents/{candidate.DocumentId}/chunks?chunkId={candidate.ChunkId}")).ToArray();
    }

    static RagRetrievedChunk ToRetrievedChunk(RetrievalCandidate candidate)
    {
        return new RagRetrievedChunk
        {
            ChunkId = candidate.ChunkId,
            DocumentId = candidate.DocumentId,
            KnowledgeBaseId = candidate.KnowledgeBaseId,
            Title = candidate.Title,
            ChunkIndex = candidate.ChunkIndex,
            ContentPreview = Preview(candidate.Content, 220),
            VectorScore = Round(candidate.VectorScore),
            KeywordScore = Round(candidate.KeywordScore),
            FusedScore = Round(candidate.FusedScore),
            RerankScore = candidate.RerankScore is null ? null : Round(candidate.RerankScore.Value)
        };
    }

    static double CalculateConfidence(IReadOnlyList<RetrievalCandidate> context, RagAnswerDraft draft)
    {
        var retrievalScore = context.Count == 0
            ? 0
            : context.Take(3).Average(candidate => candidate.RerankScore ?? candidate.FusedScore);

        var confidence =
            0.4 * Clamp01(retrievalScore) +
            0.3 * Clamp01(draft.CompletenessScore) +
            0.3 * Clamp01(draft.SelfScore);

        return Round(confidence);
    }

    static IReadOnlyCollection<string> NormalizePrincipals(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static double ClampWeight(double value) => value <= 0 ? 0 : value;

    static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    static double Round(double value) => Math.Round(Clamp01(value), 4);

    static string Preview(string value, int maxLength)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }
}
