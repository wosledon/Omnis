using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Omnis.DocumentX.Knowledge;
using Omnis.EfCore.Npgsql.Contracts;
using Omnis.EfCore.Npgsql.Initialization;
using Omnis.EfCore.Npgsql.Rag.Services;
using Omnis.Retrieval.Rag;

namespace Omnis.EfCore.Npgsql.Knowledge.Services;

/// <summary>
/// PostgreSQL 知识管理模块依赖注册扩展。
/// </summary>
public static class PostgresKnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddPostgresKnowledgeManagement(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BindOptions(configuration);

        services.AddDocumentProcessing();
        services.AddSingleton(options);
        services.AddDbContext<OmnisNpgsqlDbContext>(builder => builder.UseNpgsql(options.ConnectionString));
        services.AddSingleton<IKnowledgeVectorizer, DeterministicVectorizer>();
        services.AddScoped<IKnowledgeVectorStore>(sp => CreateVectorStore(sp, options));
        services.AddScoped<IKnowledgeService, PostgresKnowledgeService>();
        services.AddRagEngineCore();
        services.AddScoped<IHybridRetriever, PostgresHybridRetriever>();
        services.AddScoped<IRagObservationSink, PostgresRagObservationSink>();
        services.AddSingleton<IHostedService, PostgresSchemaInitializer>();

        return services;
    }

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
