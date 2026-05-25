using Omnis.Retrieval.Rag;

namespace Omnis.Api.Endpoints;

public static class RagEndpoints
{
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
