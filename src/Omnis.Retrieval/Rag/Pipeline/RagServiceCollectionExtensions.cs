using Microsoft.Extensions.DependencyInjection;

namespace Omnis.Retrieval.Rag;

public static class RagServiceCollectionExtensions
{
    public static IServiceCollection AddRagEngineCore(this IServiceCollection services)
    {
        services.AddScoped<IRagService, RagService>();
        services.AddScoped<IRagQueryRewriter, SimpleRagQueryRewriter>();
        services.AddScoped<IRagReranker, DefaultRagReranker>();
        services.AddScoped<IRagPromptBuilder, DefaultRagPromptBuilder>();
        services.AddScoped<IRagAnswerGenerator, ExtractiveRagAnswerGenerator>();
        services.AddScoped<IRagObservationSink, NullRagObservationSink>();

        return services;
    }
}
