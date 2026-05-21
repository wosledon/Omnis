using Microsoft.Extensions.DependencyInjection;

namespace Omnis.DocumentX.Knowledge;

/// <summary>
/// 知识处理相关依赖注册扩展。
/// </summary>
public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>
    /// 注册文档解析、分片和 embedding id 生成管线。
    /// </summary>
    public static IServiceCollection AddDocumentProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<ITextChunker, ParagraphTextChunker>();
        services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();

        return services;
    }

    /// <summary>
    /// 注册内存版知识管理实现，主要用于本地原型和轻量测试。
    /// </summary>
    public static IServiceCollection AddInMemoryKnowledgeManagement(this IServiceCollection services)
    {
        services.AddDocumentProcessing();
        services.AddSingleton<IKnowledgeService, InMemoryKnowledgeService>();

        return services;
    }
}
