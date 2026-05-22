using Microsoft.Extensions.DependencyInjection;

namespace Omnis.DocumentX.Knowledge;

/// <summary>
/// 知识文档处理相关依赖注册扩展。
/// </summary>
public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>
    /// 注册文档解析、分片和 embedding id 生成管线；数据持久化由数据库模块负责。
    /// </summary>
    public static IServiceCollection AddDocumentProcessing(this IServiceCollection services)
    {
        services.AddSingleton<IDocumentTextExtractor, DocumentTextExtractor>();
        services.AddSingleton<ITextChunker, ParagraphTextChunker>();
        services.AddSingleton<IEmbeddingGenerator, DeterministicEmbeddingGenerator>();

        return services;
    }
}
