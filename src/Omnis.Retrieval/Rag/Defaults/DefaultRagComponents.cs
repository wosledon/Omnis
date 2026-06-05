using System.Text;
using System.Text.RegularExpressions;

namespace Omnis.Retrieval.Rag;

/// <summary>
/// 默认查询改写、重排序、Prompt 组装和答案生成实现。
/// </summary>
internal sealed class SimpleRagQueryRewriter : IRagQueryRewriter
{
    /// <inheritdoc />
    public Task<RewrittenQuery> RewriteAsync(RagAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var question = Normalize(request.Question);
        var history = request.ConversationHistory
            .Where(message => IsUseful(message.Role) && !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(Math.Max(0, request.Options.MaxHistoryTurns))
            .Select(message => Normalize(message.Content))
            .Where(content => content.Length > 0)
            .TakeLast(4)
            .ToArray();

        var query = history.Length == 0
            ? question
            : $"{string.Join(' ', history)} {question}";

        return Task.FromResult(new RewrittenQuery(question, Normalize(query)));
    }

    static bool IsUseful(string role)
    {
        return role.Equals("user", StringComparison.OrdinalIgnoreCase)
            || role.Equals("assistant", StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string value)
    {
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}

internal sealed class DefaultRagReranker : IRagReranker
{
    /// <inheritdoc />
    public Task<IReadOnlyList<RetrievalCandidate>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalCandidate> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        var queryTerms = TextScoring.Tokenize(query).ToArray();
        var result = candidates
            .Select(candidate =>
            {
                var coverage = TextScoring.TermCoverage(queryTerms, candidate.Content);
                var score = 0.75 * candidate.FusedScore + 0.25 * coverage;
                return candidate with { RerankScore = score };
            })
            .OrderByDescending(candidate => candidate.RerankScore)
            .ThenBy(candidate => candidate.ChunkIndex)
            .Take(Math.Max(1, topN))
            .ToArray();

        return Task.FromResult<IReadOnlyList<RetrievalCandidate>>(result);
    }
}

internal sealed class DefaultRagPromptBuilder : IRagPromptBuilder
{
    /// <inheritdoc />
    public string BuildPrompt(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Omnis, an enterprise customer-service RAG assistant.");
        builder.AppendLine("Answer only with the authorized context below when strict knowledge boundary is enabled.");
        builder.AppendLine("Every factual claim from retrieved knowledge must include citation markers like 【source-1】.");
        builder.AppendLine();
        builder.AppendLine($"Original question: {rewrittenQuery.OriginalQuestion}");
        builder.AppendLine($"Rewritten query: {rewrittenQuery.Query}");
        builder.AppendLine();

        var history = request.ConversationHistory
            .TakeLast(Math.Max(0, request.Options.MaxHistoryTurns))
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .ToArray();

        if (history.Length > 0)
        {
            builder.AppendLine("Conversation history:");
            foreach (var message in history)
            {
                builder.AppendLine($"- {message.Role}: {message.Content}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Authorized retrieved context:");
        for (var index = 0; index < context.Count; index++)
        {
            var citation = citations[index];
            var candidate = context[index];
            builder.AppendLine($"[{citation.Id}] {candidate.Title} / chunk {candidate.ChunkIndex}");
            builder.AppendLine(candidate.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }
}

internal sealed class ExtractiveRagAnswerGenerator : IRagAnswerGenerator
{
    /// <inheritdoc />
    public Task<RagAnswerDraft> GenerateAsync(
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
            return Task.FromResult(new RagAnswerDraft
            {
                Answer = empty,
                RawOutput = empty,
                CompletenessScore = 0,
                SelfScore = 0,
                CitationIds = []
            });
        }

        var selected = context.Take(Math.Min(3, context.Count)).ToArray();
        var selectedCitations = citations.Take(selected.Length).ToArray();
        var builder = new StringBuilder();
        builder.Append("根据知识库资料：");

        for (var index = 0; index < selected.Length; index++)
        {
            var sentence = BestSentence(rewrittenQuery.Query, selected[index].Content);
            builder.Append(' ');
            builder.Append(sentence);
            builder.Append($"【{selectedCitations[index].Id}】");
        }

        var answer = builder.ToString();
        return Task.FromResult(new RagAnswerDraft
        {
            Answer = answer,
            RawOutput = answer,
            CompletenessScore = selected.Length >= 2 ? 0.86 : 0.72,
            SelfScore = selected.Average(candidate => candidate.RerankScore ?? candidate.FusedScore),
            CitationIds = selectedCitations.Select(citation => citation.Id).ToArray()
        });
    }

    public async IAsyncEnumerable<RagAnswerDraftStreamChunk> GenerateStreamAsync(
        RagAnswerRequest request,
        RewrittenQuery rewrittenQuery,
        string prompt,
        IReadOnlyList<RetrievalCandidate> context,
        IReadOnlyList<RagCitation> citations,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var draft = await GenerateAsync(request, rewrittenQuery, prompt, context, citations, cancellationToken);
        foreach (var token in draft.Answer.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RagAnswerDraftStreamChunk(token + " ", false);
        }

        yield return new RagAnswerDraftStreamChunk(string.Empty, true, draft);
    }

    static string BestSentence(string query, string content)
    {
        var queryTerms = TextScoring.Tokenize(query).ToArray();
        var sentences = Regex.Split(content, @"(?<=[。！？.!?])\s+|\r?\n+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .ToArray();

        if (sentences.Length == 0)
        {
            return Trim(content, 180);
        }

        var best = sentences
            .OrderByDescending(sentence => TextScoring.TermCoverage(queryTerms, sentence))
            .ThenBy(sentence => sentence.Length)
            .First();

        return Trim(best, 180);
    }

    static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

internal sealed class NullRagObservationSink : IRagObservationSink
{
    /// <inheritdoc />
    public Task SaveAsync(RagObservationRecord record, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal static class TextScoring
{
    /// <summary>
    /// 简单分词和覆盖率评分工具，供默认 rerank 和检索使用。
    /// </summary>
    static readonly char[] Separators =
    [
        ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}',
        '"', '\'', '/', '\\', '|', '-', '_', '+', '=', '*', '&', '^', '%', '$', '#', '@',
        '，', '。', '；', '：', '！', '？', '（', '）', '【', '】', '、'
    ];

    public static IEnumerable<string> Tokenize(string value)
    {
        var normalized = value.ToLowerInvariant();
        var wordTokens = normalized
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1);

        foreach (var token in wordTokens)
        {
            yield return token;
        }

        foreach (var gram in CjkBigrams(normalized))
        {
            yield return gram;
        }
    }

    public static double TermCoverage(IReadOnlyCollection<string> queryTerms, string content)
    {
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var contentTerms = Tokenize(content).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = queryTerms.Count(term => contentTerms.Contains(term));
        return (double)matched / queryTerms.Count;
    }

    static IEnumerable<string> CjkBigrams(string value)
    {
        var chars = value.Where(IsCjk).ToArray();
        for (var index = 0; index < chars.Length - 1; index++)
        {
            yield return new string([chars[index], chars[index + 1]]);
        }
    }

    static bool IsCjk(char value)
    {
        return value is >= '\u4e00' and <= '\u9fff';
    }
}
