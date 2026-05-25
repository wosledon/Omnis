using Microsoft.Extensions.DependencyInjection;

namespace Omnis.Retrieval.Rag;

/// <summary>
/// RAG 核心组件的依赖注入注册扩展。
/// </summary>
public static class RagServiceCollectionExtensions
{
    /// <summary>
    /// 注册默认的 RAG 查询改写、检索、重排、生成和观测实现。
    /// </summary>
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
