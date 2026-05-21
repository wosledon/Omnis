using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Omnis.EfCore.Npgsql.Contracts;

namespace Omnis.EfCore.Npgsql.Services;

/// <summary>
/// PostgreSQL 知识模块启动初始化器，通过 EF Core 模型创建数据库结构。
/// </summary>
internal sealed class PostgresKnowledgeInitializer(
    IServiceScopeFactory scopeFactory,
    PostgresKnowledgeOptions options
) : IHostedService
{
    /// <summary>
    /// 应用启动时根据配置调用 EF Core 初始化，不再维护手写建表 SQL。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.AutoCreateDatabase && !options.AutoCreateTables)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OmnisNpgsqlDbContext>();

        // EnsureCreatedAsync 会按照实体映射创建库表；生产环境可替换为正式迁移流程。
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>
    /// 当前初始化器没有后台资源需要释放。
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
