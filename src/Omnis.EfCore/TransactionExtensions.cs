using Microsoft.EntityFrameworkCore;
using Omnis.EfCore.Services;
using System.Data;

namespace Omnis.EfCore;

/// <summary>
/// 事务相关的扩展方法
/// </summary>
public static class TransactionExtensions
{
    /// <summary>
    /// 使用事务执行操作，自动提交或回滚
    /// </summary>
    /// <param name="unitOfWork">单元OfWork</param>
    /// <param name="action">事务操作</param>
    /// <param name="isolationLevel">隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public static async Task WithTransactionAsync(
        this IUnitOfWork unitOfWork,
        Func<IUnitOfWork, CancellationToken, Task> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        try
        {
            await action(unitOfWork, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 使用事务执行操作，自动提交或回滚（带返回值）
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="unitOfWork">单元OfWork</param>
    /// <param name="func">事务操作函数</param>
    /// <param name="isolationLevel">隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public static async Task<T> WithTransactionAsync<T>(
        this IUnitOfWork unitOfWork,
        Func<IUnitOfWork, CancellationToken, Task<T>> func,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await unitOfWork.BeginTransactionAsync(isolationLevel, cancellationToken);
        try
        {
            var result = await func(unitOfWork, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
