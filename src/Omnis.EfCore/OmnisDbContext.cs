using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Omnis.EfCore.Contracts;
using Omnis.EfCore.Services;

namespace Omnis.EfCore;

public class OmnisDbContext(
    DbContextOptions options,
    IAuditContextProvider? auditContextProvider
) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 配置所有软删除实体的全局查询过滤器
        ApplySoftDeleteFilter(modelBuilder);

        // 配置版本字段
        ConfigureVersionableEntities(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditInfo();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInfo();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 在保存前自动填充审计字段
    /// </summary>
    void ApplyAuditInfo()
    {
        var userId = auditContextProvider?.GetCurrentUserId();

        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    // 如果业务服务已经传入审计用户，则保留业务侧的明确值。
                    auditableEntity.CreatedBy ??= userId;
                    auditableEntity.CreatedAt ??= now;
                }
                else
                {
                    // 后台任务没有当前用户时，不覆盖服务层设置的操作人。
                    if (userId.HasValue)
                    {
                        auditableEntity.UpdatedBy = userId;
                    }

                    auditableEntity.UpdatedAt = now;
                }
            }
        }
    }

    /// <summary>
    /// 配置版本控制实体的并发令牌
    /// </summary>
    void ConfigureVersionableEntities(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IVersionable).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IVersionable.RowVersion))
                    .IsRowVersion();
            }
        }
    }

    /// <summary>
    /// 应用软删除全局查询过滤器
    /// </summary>
    void ApplySoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeleteEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(ISoftDeleteEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Equal(property, Expression.Constant(false)), parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    /// <summary>
    /// 软删除实体的扩展方法 - 将 IsDeleted 设置为 true 而不是真正删除
    /// </summary>
    void SoftDelete<TEntity>(TEntity entity) where TEntity : class, ISoftDeleteEntity
    {
        entity.IsDeleted = true;
        Entry(entity).State = EntityState.Modified;
    }

    /// <summary>
    /// 包含已删除实体的查询（禁用软删除过滤器）
    /// </summary>
    public IQueryable<TEntity> WithDeleted<TEntity>() where TEntity : class
    {
        return Set<TEntity>().IgnoreQueryFilters();
    }

    /// <summary>
    /// 仅查询已删除的实体
    /// </summary>
    public IQueryable<TEntity> OnlyDeleted<TEntity>() where TEntity : class, ISoftDeleteEntity
    {
        return Set<TEntity>().IgnoreQueryFilters().Where(e => e.IsDeleted);
    }

    /// <summary>
    /// 批量软删除
    /// </summary>
    public async Task<int> SoftDeleteRangeAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        where TEntity : class, ISoftDeleteEntity
    {
        foreach (var entity in entities)
        {
            entity.IsDeleted = true;
            Entry(entity).State = EntityState.Modified;
        }

        return await SaveChangesAsync(cancellationToken);
    }
}
