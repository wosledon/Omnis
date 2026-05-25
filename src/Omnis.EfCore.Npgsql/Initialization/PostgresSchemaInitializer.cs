using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Omnis.EfCore.Npgsql.Contracts;

namespace Omnis.EfCore.Npgsql.Initialization;

/// <summary>
/// PostgreSQL schema 初始化器，负责首次启动时创建当前项目声明的数据库表和索引。
/// </summary>
internal sealed class PostgresSchemaInitializer(
    IServiceScopeFactory scopeFactory,
    PostgresKnowledgeOptions options
) : IHostedService
{
    /// <summary>
    /// 应用启动时根据配置执行 EF Core 建库兜底和项目 SQL 脚本。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.AutoCreateDatabase && !options.AutoCreateTables)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OmnisNpgsqlDbContext>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await ExecuteSchemaScriptsAsync(dbContext, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    async Task ExecuteSchemaScriptsAsync(OmnisNpgsqlDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!options.AutoCreateTables && !options.AutoCreateDatabase)
        {
            return;
        }

        var sqlRoot = Path.Combine(AppContext.BaseDirectory, "sql");
        if (!Directory.Exists(sqlRoot))
        {
            return;
        }

        var sqlFiles = Directory.GetFiles(sqlRoot, "*.sql", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var file in sqlFiles)
        {
            var sql = await File.ReadAllTextAsync(file, cancellationToken);
            await ExecuteSqlAsync(dbContext, sql, cancellationToken);
        }
    }

    static async Task ExecuteSqlAsync(
        OmnisNpgsqlDbContext dbContext,
        string sql,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = System.Data.CommandType.Text;
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
