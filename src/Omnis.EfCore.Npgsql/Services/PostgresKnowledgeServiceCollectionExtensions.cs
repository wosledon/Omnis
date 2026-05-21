using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Omnis.DocumentX.Knowledge;
using Omnis.EfCore.Npgsql.Contracts;

namespace Omnis.EfCore.Npgsql.Services;

/// <summary>
/// PostgreSQL 知识管理模块依赖注册扩展。
/// </summary>
public static class PostgresKnowledgeServiceCollectionExtensions
{
    /// <summary>
    /// 注册 PostgreSQL 知识服务、文档处理管线、向量存储和启动初始化器。
    /// </summary>
    public static IServiceCollection AddPostgresKnowledgeManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BindOptions(configuration);

        // 文档处理管线放在 DocumentX 项目，PG 服务复用它来解析和分片。
        services.AddDocumentProcessing();
        services.AddSingleton(options);

        // 使用 EF Core DbContext 作为唯一数据库入口，业务代码只操作实体集合。
        services.AddDbContext<OmnisNpgsqlDbContext>(builder => builder.UseNpgsql(options.ConnectionString));

        services.AddSingleton<IKnowledgeVectorizer, DeterministicVectorizer>();
        services.AddScoped<IKnowledgeVectorStore>(sp => CreateVectorStore(sp, options));
        services.AddScoped<IKnowledgeService, PostgresKnowledgeService>();
        services.AddSingleton<IHostedService, PostgresKnowledgeInitializer>();

        return services;
    }

    /// <summary>
    /// 根据配置选择向量存储实现，Qdrant/Milvus 当前保留扩展点。
    /// </summary>
    static IKnowledgeVectorStore CreateVectorStore(IServiceProvider services, PostgresKnowledgeOptions options)
    {
        return options.VectorProvider.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pgsql" => new PostgreSqlKnowledgeVectorStore(
                services.GetRequiredService<OmnisNpgsqlDbContext>()),
            "qdrant" => new UnsupportedExternalVectorStore("Qdrant"),
            "milvus" => new UnsupportedExternalVectorStore("Milvus"),
            var provider => new UnsupportedExternalVectorStore(provider)
        };
    }

    /// <summary>
    /// 从配置读取知识模块选项，并提供本地 Docker PG 默认值。
    /// </summary>
    static PostgresKnowledgeOptions BindOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(PostgresKnowledgeOptions.SectionName);
        var options = new PostgresKnowledgeOptions();

        options.ConnectionString =
            section["ConnectionString"]
            ?? configuration.GetConnectionString("OmnisPostgres")
            ?? options.ConnectionString;

        if (bool.TryParse(section["AutoCreateDatabase"], out var autoCreateDatabase))
        {
            options.AutoCreateDatabase = autoCreateDatabase;
        }

        if (bool.TryParse(section["AutoCreateTables"], out var autoCreateTables))
        {
            options.AutoCreateTables = autoCreateTables;
        }

        options.VectorProvider = section["VectorProvider"] ?? options.VectorProvider;

        if (int.TryParse(section["EmbeddingDimensions"], out var dimensions))
        {
            options.EmbeddingDimensions = dimensions;
        }

        return options;
    }
}
