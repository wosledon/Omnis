using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Omnis.Contracts.Llm;
using Omnis.Llm;
using Omnis.Retrieval.Rag;

namespace Omnis.EfCore.Npgsql.Rag.Services;

/// <summary>
/// Uses the configured LLM gateway to turn retrieved knowledge chunks into a customer-service answer.
/// </summary>
internal sealed class LlmRagAnswerGenerator(ILlmGateway llmGateway) : IRagAnswerGenerator
{
    static readonly Regex CitationRegex = new(@"\bsource-\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<RagAnswerDraft> GenerateAsync(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        string prompt,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        CancellationToken cancellationToken = default)
    {
        if (context.Count == 0)
        {
            const string empty = "抱歉，我没有在知识库中找到相关内容。建议转人工处理。";
            return new RagAnswerDraft
            {
                Answer = empty,
                RawOutput = empty,
                CompletenessScore = 0,
                SelfScore = 0,
                CitationIds = []
            };
        }

        var response = await llmGateway.CompleteAsync(new LlmCompletionRequest(
            request.TenantId,
            request.WorkspaceId,
            request.ApplicationId,
            [
                new LlmChatMessage(LlmMessageRole.System, BuildSystemPrompt(request)),
                new LlmChatMessage(LlmMessageRole.User, BuildUserPrompt(rewrittenQuery, context, citations, prompt))
            ],
            Temperature: 0.2,
            MaxTokens: 900,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = "rag",
                ["conversationId"] = request.ConversationId ?? string.Empty,
                ["messageId"] = request.MessageId ?? string.Empty,
                ["knowledgeBaseIds"] = string.Join(",", request.KnowledgeBaseIds)
            }), cancellationToken);

        var answer = NormalizeAnswer(response.Content);
        var citationIds = ExtractCitationIds(answer, citations);
        if (citationIds.Length == 0 && citations.Count > 0)
        {
            citationIds = [citations[0].Id];
            answer = $"{answer} [{citations[0].Id}]";
        }

        var retrievalScore = context.Take(Math.Min(3, context.Count))
            .Average(candidate => candidate.RerankScore ?? candidate.FusedScore);

        return new RagAnswerDraft
        {
            Answer = answer,
            RawOutput = response.Content,
            CompletenessScore = answer.Length > 30 ? 0.9 : 0.65,
            SelfScore = Math.Max(0.1, Math.Min(1, retrievalScore)),
            CitationIds = citationIds
        };
    }

    public async IAsyncEnumerable<RagAnswerDraftStreamChunk> GenerateStreamAsync(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        string prompt,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (context.Count == 0)
        {
            const string empty = "抱歉，我没有在知识库中找到相关内容。建议转人工处理。";
            foreach (var token in SplitForCompatStream(empty))
            {
                yield return new RagAnswerDraftStreamChunk(token, false);
            }

            yield return new RagAnswerDraftStreamChunk(string.Empty, true, new RagAnswerDraft
            {
                Answer = empty,
                RawOutput = empty,
                CompletenessScore = 0,
                SelfScore = 0,
                CitationIds = []
            });
            yield break;
        }

        var builder = new StringBuilder();
        await foreach (var chunk in llmGateway.StreamAsync(new LlmCompletionRequest(
            request.TenantId,
            request.WorkspaceId,
            request.ApplicationId,
            [
                new LlmChatMessage(LlmMessageRole.System, BuildSystemPrompt(request)),
                new LlmChatMessage(LlmMessageRole.User, BuildUserPrompt(rewrittenQuery, context, citations, prompt))
            ],
            Temperature: 0.2,
            MaxTokens: 900,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = "rag",
                ["conversationId"] = request.ConversationId ?? string.Empty,
                ["messageId"] = request.MessageId ?? string.Empty,
                ["knowledgeBaseIds"] = string.Join(",", request.KnowledgeBaseIds),
                ["stream"] = "true"
            }), cancellationToken))
        {
            if (chunk.IsCompleted)
            {
                continue;
            }

            builder.Append(chunk.ContentDelta);
            yield return new RagAnswerDraftStreamChunk(chunk.ContentDelta, false);
        }

        var raw = builder.ToString();
        var answer = NormalizeAnswer(raw);
        var citationIds = ExtractCitationIds(answer, citations);
        if (citationIds.Length == 0 && citations.Count > 0)
        {
            citationIds = [citations[0].Id];
            answer = $"{answer} [{citations[0].Id}]";
        }

        var retrievalScore = context.Take(Math.Min(3, context.Count))
            .Average(candidate => candidate.RerankScore ?? candidate.FusedScore);

        yield return new RagAnswerDraftStreamChunk(string.Empty, true, new RagAnswerDraft
        {
            Answer = answer,
            RawOutput = raw,
            CompletenessScore = answer.Length > 30 ? 0.9 : 0.65,
            SelfScore = Math.Max(0.1, Math.Min(1, retrievalScore)),
            CitationIds = citationIds
        });
    }

    static string BuildSystemPrompt(RagAnswerRequest request)
    {
        var boundary = request.Options.StrictKnowledgeBoundary
            ? "必须严格基于授权知识库上下文回答；上下文没有的信息要明确说未找到，不要编造。"
            : "优先基于授权知识库上下文回答；必要时可以补充常识，但要标明不来自知识库。";

        return string.Join('\n',
            "你是 Omnis 企业 AI 客服助手。",
            boundary,
            "回答要自然、清楚、面向客户，避免暴露内部检索过程。",
            "凡是来自知识库的事实都必须在句末加入引用标记，例如 [source-1]。",
            "只能使用提供的 source-* 引用编号，不要创造新的引用编号。");
    }

    static string BuildUserPrompt(
        RewrittenQuery rewrittenQuery,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        string debugPrompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"用户原始问题：{rewrittenQuery.OriginalQuestion}");
        builder.AppendLine($"检索问题：{rewrittenQuery.Query}");
        builder.AppendLine();
        builder.AppendLine("授权知识库上下文：");

        for (var index = 0; index < context.Count; index++)
        {
            var citation = citations[index];
            var candidate = context[index];
            builder.AppendLine($"[{citation.Id}] 文档：{candidate.Title}，分片：{candidate.ChunkIndex}");
            builder.AppendLine(candidate.Content);
            builder.AppendLine();
        }

        builder.AppendLine("请直接给出客服回复。不要输出 JSON，不要复述上下文列表。");
        builder.AppendLine();
        builder.AppendLine("调试用完整 Prompt 快照：");
        builder.AppendLine(debugPrompt);

        return builder.ToString();
    }

    static string NormalizeAnswer(string value)
    {
        var normalized = string.Join('\n', value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));

        return normalized.Length == 0
            ? "抱歉，我暂时无法生成答案，建议转人工处理。"
            : normalized;
    }

    static string[] ExtractCitationIds(string answer, IReadOnlyList<RagCitation> citations)
    {
        var valid = citations.Select(citation => citation.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return CitationRegex.Matches(answer)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(valid.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static IEnumerable<string> SplitForCompatStream(string value)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token + " ";
        }
    }
}
