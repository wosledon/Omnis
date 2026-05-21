namespace Omnis.EfCore.Npgsql.Contracts;

/// <summary>
/// PostgreSQL 知识管理模块配置项。
/// </summary>
public sealed class PostgresKnowledgeOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Knowledge:Postgres";

    /// <summary>业务数据库连接串，默认匹配本地 Docker PostgreSQL。</summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=omnis;Username=postgres;Password=123456";

    /// <summary>启动时是否自动创建业务数据库。</summary>
    public bool AutoCreateDatabase { get; set; } = true;

    /// <summary>启动时是否自动创建知识模块表结构。</summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>向量存储提供方；当前默认 PostgreSql，预留 Qdrant/Milvus。</summary>
    public string VectorProvider { get; set; } = "PostgreSql";

    /// <summary>确定性占位向量维度，后续接真实 embedding 模型时可调整或废弃。</summary>
    public int EmbeddingDimensions { get; set; } = 64;
}
