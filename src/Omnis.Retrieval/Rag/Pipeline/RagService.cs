using System.Diagnostics;
using System.Runtime.CompilerServices;
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

        var pipeline = await PrepareAsync(request, cancellationToken);
        var generationWatch = Stopwatch.StartNew();
        var draft = pipeline.BoundaryTriggered
            ? CreateBoundaryDraft(pipeline.RewrittenQuery.Query)
            : await answerGenerator.GenerateAsync(request, pipeline.RewrittenQuery, pipeline.Prompt, pipeline.Context, pipeline.Citations, cancellationToken);
        generationWatch.Stop();
        pipeline.Total.Stop();

        var response = CreateResponse(request, pipeline, draft, generationWatch.ElapsedMilliseconds, pipeline.Total.ElapsedMilliseconds);
        await SaveObservationAsync(request, response, HasHallucination(draft, pipeline.Citations), cancellationToken);
        return response;
    }

    public async IAsyncEnumerable<RagAnswerStreamChunk> AnswerStreamAsync(
        RagAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Validate(request);

        var pipeline = await PrepareAsync(request, cancellationToken);
        var generationWatch = Stopwatch.StartNew();
        RagAnswerDraft? draft;

        if (pipeline.BoundaryTriggered)
        {
            draft = CreateBoundaryDraft(pipeline.RewrittenQuery.Query);
            foreach (var token in SplitForCompatStream(draft.Answer))
            {
                yield return new RagAnswerStreamChunk(token, false);
            }
        }
        else
        {
            draft = null;
            await foreach (var chunk in answerGenerator.GenerateStreamAsync(
                request,
                pipeline.RewrittenQuery,
                pipeline.Prompt,
                pipeline.Context,
                pipeline.Citations,
                cancellationToken))
            {
                if (chunk.IsCompleted)
                {
                    draft = chunk.Completed;
                    continue;
                }

                yield return new RagAnswerStreamChunk(chunk.ContentDelta, false);
            }

            draft ??= CreateBoundaryDraft(pipeline.RewrittenQuery.Query);
        }

        generationWatch.Stop();
        pipeline.Total.Stop();

        var response = CreateResponse(request, pipeline, draft, generationWatch.ElapsedMilliseconds, pipeline.Total.ElapsedMilliseconds);
        await SaveObservationAsync(request, response, HasHallucination(draft, pipeline.Citations), cancellationToken);
        yield return new RagAnswerStreamChunk(string.Empty, true, response);
    }

    async Task<RagPipelineState> PrepareAsync(RagAnswerRequest request, CancellationToken cancellationToken)
    {
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

        var retrieved = context.Select(ToRetrievedChunk).ToArray();

        return new RagPipelineState(
            total,
            rewrittenQuery,
            context,
            citations,
            retrieved,
            boundaryTriggered,
            prompt,
            retrievalWatch.ElapsedMilliseconds);
    }

    static RagAnswerResponse CreateResponse(
        RagAnswerRequest request,
        RagPipelineState pipeline,
        RagAnswerDraft draft,
        long generationDurationMs,
        long totalDurationMs)
    {
        var validCitationIds = pipeline.Citations.Select(citation => citation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCitationIds = draft.CitationIds.Where(validCitationIds.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var usedCitations = pipeline.Citations.Where(citation => usedCitationIds.Contains(citation.Id, StringComparer.OrdinalIgnoreCase)).ToArray();
        var confidence = pipeline.BoundaryTriggered
            ? 0
            : CalculateConfidence(pipeline.Context, draft);

        return new RagAnswerResponse
        {
            Answer = draft.Answer,
            OriginalQuestion = pipeline.RewrittenQuery.OriginalQuestion,
            RewrittenQuery = pipeline.RewrittenQuery.Query,
            ConfidenceScore = confidence,
            HandoffSuggested = confidence < request.Options.HandoffConfidenceThreshold,
            KnowledgeBoundaryTriggered = pipeline.BoundaryTriggered,
            Citations = usedCitations,
            RetrievedChunks = pipeline.Retrieved,
            Debug = new RagDebugTrace
            {
                Prompt = pipeline.Prompt,
                LlmRawOutput = draft.RawOutput,
                RetrievalDurationMs = pipeline.RetrievalDurationMs,
                GenerationDurationMs = generationDurationMs,
                TotalDurationMs = totalDurationMs
            }
        };
    }

    static bool HasHallucination(RagAnswerDraft draft, IReadOnlyList<RagCitation> citations)
    {
        var validCitationIds = citations.Select(citation => citation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return draft.CitationIds.Any(id => !validCitationIds.Contains(id));
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

    static IEnumerable<string> SplitForCompatStream(string value)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token + " ";
        }
    }

    sealed record RagPipelineState(
        Stopwatch Total,
        RewrittenQuery RewrittenQuery,
        IReadOnlyList<RetrievalCandidate> Context,
        IReadOnlyList<RagCitation> Citations,
        IReadOnlyList<RagRetrievedChunk> Retrieved,
        bool BoundaryTriggered,
        string Prompt,
        long RetrievalDurationMs);
}
