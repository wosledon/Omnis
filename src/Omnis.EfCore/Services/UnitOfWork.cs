using Microsoft.EntityFrameworkCore;
using Omnis.EfCore.Contracts;
using static Omnis.EfCore.OmnisEfCoreExtensions;

namespace Omnis.EfCore.Services;

public class UnitOfWork<TDbContext>(
    TDbContext db
) : IUnitOfWork
    where TDbContext : OmnisDbContext
{
    public IQueryable<T> Q<T>() where T : class => db.Set<T>().AsNoTracking();

    public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        return await db.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        db.Update(entity);
    }

    public async Task UpdateRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        db.UpdateRange(entities);
    }

    public async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class, ISoftDeleteEntity
    {
        db.SoftDelete<T>(entity);
    }

    public async Task DeleteRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class, ISoftDeleteEntity
    {
        await db.SoftDeleteRangeAsync<T>(entities, cancellationToken);
    }

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        await db.Set<T>().AddAsync(entity, cancellationToken);
    }

    public async Task AddRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        await db.Set<T>().AddRangeAsync(entities, cancellationToken);
    }

}