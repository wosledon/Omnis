using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Omnis.EfCore.Contracts;
using Omnis.EfCore.Services;

namespace Omnis.EfCore;

public static class OmnisEfCoreExtensions
{
    extension(IServiceCollection services)
    {
        public void AddEfCore<TContext>(Action<DbContextOptionsBuilder> optionsAction)
            where TContext : OmnisDbContext
        {
            services.AddDbContext<TContext>(optionsAction);
            services.AddScoped<IAuditContextProvider, HttpContextAuditContextProvider>();
            services.AddScoped<ITransactionManager, TransactionManager>();
            services.AddScoped<IUnitOfWork, UnitOfWork<TContext>>();
        }
    }
}
