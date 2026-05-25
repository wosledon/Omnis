using Microsoft.EntityFrameworkCore;
using Omnis.Contracts.Knowledge;
using Omnis.EfCore.Npgsql.Contracts;
using Omnis.Retrieval.Rag;

namespace Omnis.EfCore.Npgsql.Rag.Services;

internal sealed class PostgresHybridRetriever(
    OmnisNpgsqlDbContext dbContext,
    IKnowledgeVectorizer vectorizer) : IHybridRetriever
{
    static readonly DocumentPermission[] ReadPermissions =
    [
        DocumentPermission.Read,
        DocumentPermission.Edit,
        DocumentPermission.Delete,
        DocumentPermission.Share,
        DocumentPermission.Admin
    ];

    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        HybridSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        var queryVector = vectorizer.Vectorize(request.Query);
        var knowledgeBaseIds = request.KnowledgeBaseIds;
        var userId = request.Access.UserId;
        var groups = request.Access.Groups.ToArray();
        var roles = request.Access.Roles.ToArray();

        var authorizedRows = await (
            from chunk in dbContext.DocumentChunks.AsNoTracking()
            join document in dbContext.KnowledgeDocuments.AsNoTracking()
                on chunk.DocumentId equals document.Id
            join vector in dbContext.KnowledgeVectors.AsNoTracking()
                on chunk.Id equals vector.ChunkId
            where chunk.TenantId == request.TenantId
                && chunk.WorkspaceId == request.WorkspaceId
                && document.TenantId == request.TenantId
                && document.WorkspaceId == request.WorkspaceId
                && document.Status == DocumentStatus.Completed
                && !vector.IsDeleted
                && (knowledgeBaseIds.Length == 0 || knowledgeBaseIds.Contains(chunk.KnowledgeBaseId))
                && (
                    document.Visibility == DocumentVisibility.Public
                    || dbContext.DocumentAclEntries.AsNoTracking().Any(acl =>
                        acl.DocumentId == document.Id
                        && ReadPermissions.Contains(acl.Permission)
                        && (
                            (acl.PrincipalType == AclPrincipalType.User && acl.PrincipalId == userId)
                            || (acl.PrincipalType == AclPrincipalType.UserGroup && groups.Contains(acl.PrincipalId))
                            || (acl.PrincipalType == AclPrincipalType.Role && roles.Contains(acl.PrincipalId)))))
            select new AuthorizedChunkRow(
                chunk.Id,
                chunk.DocumentId,
                chunk.KnowledgeBaseId,
                document.Name,
                chunk.ChunkIndex,
                chunk.Content,
                vector.Vector))
            .ToArrayAsync(cancellationToken);

        if (authorizedRows.Length == 0)
        {
            return [];
        }

        var queryTerms = Tokenize(request.Query).ToArray();
        var vectorWeight = request.VectorWeight <= 0 && request.KeywordWeight <= 0 ? 0.65 : request.VectorWeight;
        var keywordWeight = request.VectorWeight <= 0 && request.KeywordWeight <= 0 ? 0.35 : request.KeywordWeight;
        var totalWeight = vectorWeight + keywordWeight;

        var candidates = authorizedRows
            .Select(row =>
            {
                var vectorScore = NormalizeCosine(Cosine(queryVector, row.Vector));
                var keywordScore = KeywordScore(queryTerms, row.Content);
                var fused = ((vectorWeight * vectorScore) + (keywordWeight * keywordScore)) / totalWeight;

                return new RetrievalCandidate
                {
                    ChunkId = row.ChunkId,
                    DocumentId = row.DocumentId,
                    KnowledgeBaseId = row.KnowledgeBaseId,
                    Title = row.Title,
                    ChunkIndex = row.ChunkIndex,
                    Content = row.Content,
                    VectorScore = vectorScore,
                    KeywordScore = keywordScore,
                    FusedScore = fused
                };
            })
            .OrderByDescending(candidate => candidate.FusedScore)
            .ThenBy(candidate => candidate.ChunkIndex)
            .Take(Math.Max(1, request.TopK))
            .ToArray();

        return candidates;
    }

    static double Cosine(double[] left, double[] right)
    {
        var length = Math.Min(left.Length, right.Length);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var index = 0; index < length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    static double NormalizeCosine(double value)
    {
        return Math.Max(0, Math.Min(1, (value + 1) / 2));
    }

    static double KeywordScore(IReadOnlyCollection<string> queryTerms, string content)
    {
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var contentTerms = Tokenize(content).ToArray();
        if (contentTerms.Length == 0)
        {
            return 0;
        }

        var contentTermSet = contentTerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matched = queryTerms.Count(term => contentTermSet.Contains(term));
        var coverage = (double)matched / queryTerms.Count;
        var density = (double)matched / Math.Sqrt(contentTerms.Length);

        return Math.Max(0, Math.Min(1, 0.75 * coverage + 0.25 * density));
    }

    static IEnumerable<string> Tokenize(string value)
    {
        var normalized = value.ToLowerInvariant();
        var tokens = normalized
            .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '|', '-', '_', '+', '=', '*', '&', '^', '%', '$', '#', '@', '，', '。', '；', '：', '！', '？', '（', '）', '【', '】', '、'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1);

        foreach (var token in tokens)
        {
            yield return token;
        }

        var cjk = normalized.Where(ch => ch is >= '\u4e00' and <= '\u9fff').ToArray();
        for (var index = 0; index < cjk.Length - 1; index++)
        {
            yield return new string([cjk[index], cjk[index + 1]]);
        }
    }

    sealed record AuthorizedChunkRow(
        Guid ChunkId,
        Guid DocumentId,
        Guid KnowledgeBaseId,
        string Title,
        int ChunkIndex,
        string Content,
        double[] Vector);
}
