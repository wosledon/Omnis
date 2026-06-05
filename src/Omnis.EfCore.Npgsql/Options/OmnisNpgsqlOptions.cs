namespace Omnis.EfCore.Npgsql.Options;

/// <summary>
/// Omnis Npgsql 适配层配置项。
/// </summary>
public sealed class OmnisNpgsqlOptions
{
    /// <summary>新的推荐配置节名称。</summary>
    public const string SectionName = "Omnis:Npgsql";

    /// <summary>业务数据库连接字符串，默认匹配本地 Docker PostgreSQL。</summary>
    public string ConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=omnis;Username=postgres;Password=123456";

    /// <summary>启动时是否自动创建业务数据库。</summary>
    public bool AutoCreateDatabase { get; set; } = true;

    /// <summary>启动时是否自动执行当前项目内置 SQL 脚本。</summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>向量存储提供方；当前默认 PostgreSql，预留 Qdrant/Milvus。</summary>
    public string VectorProvider { get; set; } = "PostgreSql";

    /// <summary>确定性占位向量维度，后续接真实 embedding 模型时可调整或废弃。</summary>
    public int EmbeddingDimensions { get; set; } = 64;
}
