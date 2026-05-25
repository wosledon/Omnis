using System.Text.Json;
using Omnis.EfCore.Npgsql.Rag.Entities;
using Omnis.Retrieval.Rag;

namespace Omnis.EfCore.Npgsql.Rag.Services;

/// <summary>
/// 将 RAG 观测记录写入 PostgreSQL。
/// </summary>
internal sealed class PostgresRagObservationSink(OmnisNpgsqlDbContext dbContext) : IRagObservationSink
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 保存一条推理观测记录到数据库。
    /// </summary>
    public async Task SaveAsync(RagObservationRecord record, CancellationToken cancellationToken = default)
    {
        dbContext.RagInferenceLogs.Add(new RagInferenceLogEntity
        {
            Id = record.Id,
            TenantId = record.TenantId,
            WorkspaceId = record.WorkspaceId,
            ApplicationId = record.ApplicationId,
            ConversationId = record.ConversationId,
            MessageId = record.MessageId,
            UserId = record.UserId,
            UserQuestion = record.UserQuestion,
            RewrittenQuery = record.RewrittenQuery,
            RetrievedChunksJson = JsonSerializer.Serialize(record.RetrievedChunks, JsonOptions),
            FinalPrompt = record.FinalPrompt,
            LlmRawOutput = record.LlmRawOutput,
            FinalAnswer = record.FinalAnswer,
            ConfidenceScore = (decimal)Math.Round(record.ConfidenceScore, 4),
            CitationSourceIds = record.CitationSourceIds,
            HasHallucination = record.HasHallucination,
            RetrievalDurationMs = ToInt(record.RetrievalDurationMs),
            GenerationDurationMs = ToInt(record.GenerationDurationMs),
            InferenceDurationMs = ToInt(record.TotalDurationMs),
            CreatedAt = record.CreatedAt.UtcDateTime
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 将长整型耗时安全截断到 int 范围内。
    /// </summary>
    static int ToInt(long value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);
    }
}
