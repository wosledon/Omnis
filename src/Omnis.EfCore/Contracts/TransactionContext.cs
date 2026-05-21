using Microsoft.EntityFrameworkCore.Storage;

namespace Omnis.EfCore.Contracts;

/// <summary>
/// 表示当前事务的上下文
/// </summary>
/// <remarks>
/// 支持 using 语句，自动回滚除非显式提交：
/// <code>
/// using var tx = await unitOfWork.BeginTransactionAsync();
/// // 执行数据库操作
/// await tx.CommitAsync(); // 提交事务
/// </code>
/// </remarks>
public class TransactionContext : IAsyncDisposable
{
    private readonly IDbContextTransaction _transaction;
    private readonly bool _isNested;
    private bool _disposed;
    private bool _committed;

    internal TransactionContext(IDbContextTransaction transaction, bool isNested)
    {
        _transaction = transaction;
        _isNested = isNested;
    }

    /// <summary>
    /// 提交事务
    /// </summary>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_committed)
            throw new InvalidOperationException("事务已提交");

        if (_disposed)
            throw new ObjectDisposedException(nameof(TransactionContext));

        if (!_isNested)
        {
            await _transaction.CommitAsync(cancellationToken);
        }

        _committed = true;
    }

    /// <summary>
    /// 回滚事务
    /// </summary>
    public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TransactionContext));

        await _transaction.RollbackAsync(cancellationToken);
    }

    /// <summary>
    /// 释放资源（如果未提交则回滚）
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 如果未提交，则回滚
        if (!_committed && !_isNested && _transaction != null)
        {
            await _transaction.RollbackAsync();
        }

        await _transaction.DisposeAsync();
    }
}
