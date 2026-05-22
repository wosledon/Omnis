using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnis.EfCore.Contracts;
using static Omnis.EfCore.OmnisEfCoreExtensions;

namespace Omnis.EfCore.Services;

public class UnitOfWork<TDbContext>(TDbContext db) : IUnitOfWork
    where TDbContext : OmnisDbContext
{
    private readonly TransactionManager _transactionManager = new(db);

    private readonly TDbContext _db = db;

    public IQueryable<T> Q<T>() where T : class => _db.Set<T>().AsNoTracking();

    public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        return await _db.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        _db.Update(entity);
    }

    public async Task UpdateRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        _db.UpdateRange(entities);
    }

    public async Task DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class, ISoftDeleteEntity
    {
        _db.SoftDelete<T>(entity);
    }

    public async Task DeleteRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class, ISoftDeleteEntity
    {
        await _db.SoftDeleteRangeAsync<T>(entities, cancellationToken);
    }

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        await _db.Set<T>().AddAsync(entity, cancellationToken);
    }

    public async Task AddRangeAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : class
    {
        await _db.Set<T>().AddRangeAsync(entities, cancellationToken);
    }

    public ITransactionManager TransactionManager => _transactionManager;

    public bool HasActiveTransaction => _transactionManager.HasActiveTransaction;

    public async ValueTask<TransactionContext> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        return await _transactionManager.BeginTransactionAsync(cancellationToken);
    }

    public async ValueTask CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _transactionManager.CommitAsync(cancellationToken);
    }

    public async ValueTask RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _transactionManager.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _transactionManager.DisposeAsync();
    }
}