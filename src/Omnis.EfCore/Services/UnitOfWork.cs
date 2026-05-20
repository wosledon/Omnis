using Microsoft.EntityFrameworkCore;

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
}