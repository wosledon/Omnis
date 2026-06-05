using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Omnis.DocumentX.Knowledge;
using Omnis.EfCore.Npgsql.Channel.Services;
using Omnis.EfCore.Npgsql.Chat.Services;
using Omnis.EfCore.Npgsql.Initialization;
using Omnis.EfCore.Npgsql.Knowledge.Services;
using Omnis.EfCore.Npgsql.Llm.Services;
using Omnis.EfCore.Npgsql.Options;
using Omnis.EfCore.Npgsql.Rag.Services;
using Omnis.EfCore.Npgsql.Vector;
using Omnis.Llm;
using Omnis.Retrieval.Rag;
using Omnis.Workflow.Channel;
using Omnis.Workflow.Chat;

namespace Omnis.EfCore.Npgsql;

/// <summary>
/// Omnis PostgreSQL/Npgsql 基础设施依赖注册入口。
/// </summary>
public static class OmnisNpgsqlServiceCollectionExtensions
{
    /// <summary>
    /// 注册当前 PostgreSQL 适配层提供的基础设施能力，包括知识管理、RAG 检索观测、对话引擎和 schema 初始化。
    /// </summary>
    public static IServiceCollection AddOmnisNpgsqlInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BindOptions(configuration);

        services.AddDocumentProcessing();
        services.AddSingleton(options);
        services.AddDbContext<OmnisNpgsqlDbContext>(builder => builder.UseNpgsql(options.ConnectionString));

        // 知识管理与向量存储默认使用 PostgreSQL 实现，后续可通过 VectorProvider 切换外部向量库。
        services.AddSingleton<IKnowledgeVectorizer, DeterministicVectorizer>();
        services.AddScoped<IKnowledgeVectorStore>(sp => CreateVectorStore(sp, options));
        services.AddScoped<IKnowledgeService, PostgresKnowledgeService>();

        // RAG 引擎核心逻辑在 Retrieval 项目中，Npgsql 层提供检索器和观测日志落库实现。
        services.AddRagEngineCore();
        services.AddScoped<IHybridRetriever, PostgresHybridRetriever>();
        services.AddScoped<IRagAnswerGenerator, LlmRagAnswerGenerator>();
        services.AddScoped<IRagObservationSink, PostgresRagObservationSink>();

        // 对话引擎使用 PostgreSQL 保存会话、消息、反馈和人工转接记录。
        services.AddScoped<IConversationService, PostgresConversationService>();
        services.AddScoped<IChannelService, PostgresChannelService>();
        services.AddLlmGatewayCore();
        services.AddScoped<ILlmGatewayStore, PostgresLlmGatewayStore>();

        services.AddSingleton<IHostedService, PostgresSchemaInitializer>();

        return services;
    }

    /// <summary>
    /// 根据配置创建向量存储实现。MVP 默认使用 PostgreSQL，外部向量库先显式返回不支持实现。
    /// </summary>
    static IKnowledgeVectorStore CreateVectorStore(IServiceProvider services, OmnisNpgsqlOptions options)
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
    /// 绑定 PostgreSQL 相关配置，兼容 Knowledge:ConnectionString 和 ConnectionStrings:OmnisPostgres。
    /// </summary>
    static OmnisNpgsqlOptions BindOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(OmnisNpgsqlOptions.SectionName);

        var options = new OmnisNpgsqlOptions();

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
