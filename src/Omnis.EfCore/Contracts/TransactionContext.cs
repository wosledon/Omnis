namespace Omnis.EfCore.Contracts;

/// <summary>
/// 表示当前事务的上下文
/// </summary>
public class TransactionContext : IDisposable
{
    private readonly IDbContextTransaction _transaction;
    private readonly bool _isNested;
    private bool _disposed;

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
        if (_disposed)
            throw new ObjectDisposedException(nameof(TransactionContext));

        if (!_isNested)
        {
            await _transaction.CommitAsync(cancellationToken);
        }
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
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _transaction.Dispose();
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _transaction.DisposeAsync();
    }
}
