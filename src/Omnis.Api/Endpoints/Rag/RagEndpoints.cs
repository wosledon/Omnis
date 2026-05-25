using Omnis.Retrieval.Rag;

namespace Omnis.Api.Endpoints;

/// <summary>
/// RAG 对外 HTTP 接口映射。
/// </summary>
public static class RagEndpoints
{
    /// <summary>
    /// 注册 RAG 问答接口。
    /// </summary>
    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rag")
            .WithTags("RAG");

        group.MapPost("/answer", async (
            RagAnswerRequest request,
            IRagService rag,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var answer = await rag.AnswerAsync(request, cancellationToken);
                return Results.Ok(answer);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }
}
