using Microsoft.EntityFrameworkCore;

namespace Omnis.EfCore.Contracts;

/// <summary>
/// 事务管理器实现，支持事务嵌套
/// </summary>
/// <remarks>
/// 使用引用计数来管理嵌套事务：
/// - 每次 BeginTransaction 会创建新的事务并增加计数
/// - 只有最外层事务（计数为1）提交/回滚时才会操作数据库
/// - 每次 Dispose 会减少计数，只有计数为0时才真正释放
/// </remarks>
public class TransactionManager : ITransactionManager
{
    private readonly DbContext _context;
    private IDbContextTransaction? _currentTransaction;
    private int _referenceCount;
    private bool _disposed;

    public TransactionManager(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// 是否存在活动事务
    /// </summary>
    public bool HasActiveTransaction => _currentTransaction != null && _referenceCount > 0;

    /// <summary>
    /// 获取当前活动的事务上下文
    /// </summary>
    public TransactionContext? ActiveTransaction =>
        _currentTransaction != null ? new TransactionContext(_currentTransaction, _referenceCount > 1) : null;

    /// <summary>
    /// 开始新的事务
    /// </summary>
    public async ValueTask<TransactionContext> BeginTransactionAsync(
        System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // 如果已有事务，创建保存点（嵌套事务）
        if (_currentTransaction != null)
        {
            _referenceCount++;
            // EF Core 不直接支持保存点，但可以通过数据库命令实现
            // 这里返回现有事务的上下文，由调用者管理
            return new TransactionContext(_currentTransaction, isNested: true);
        }

        // 开始新的事务
        _currentTransaction = await _context.Database.BeginTransactionAsync(isolationLevel, cancellationToken);
        _referenceCount = 1;

        return new TransactionContext(_currentTransaction, isNested: false);
    }

    /// <summary>
    /// 提交当前事务
    /// </summary>
    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_currentTransaction == null)
            throw new InvalidOperationException("没有活动的事务可以提交");

        if (_referenceCount > 1)
        {
            // 嵌套事务，只减少引用计数
            _referenceCount--;
            return;
        }

        // 最外层事务，提交并释放
        try
        {
            await _currentTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            DisposeTransaction();
        }
    }

    /// <summary>
    /// 回滚当前事务
    /// </summary>
    public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_currentTransaction == null)
            throw new InvalidOperationException("没有活动的事务可以回滚");

        if (_referenceCount > 1)
        {
            // 嵌套事务，只减少引用计数
            _referenceCount--;
            return;
        }

        // 最外层事务，回滚并释放
        try
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            DisposeTransaction();
        }
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_currentTransaction != null)
        {
            // 如果还有引用，只减少计数
            if (_referenceCount > 0)
            {
                _referenceCount--;
            }

            // 如果计数为0，释放事务
            if (_referenceCount == 0)
            {
                await _currentTransaction.DisposeAsync();
                _currentTransaction = null;
            }
        }
    }

    private void DisposeTransaction()
    {
        _currentTransaction?.Dispose();
        _currentTransaction = null;
        _referenceCount = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TransactionManager));
    }

    // 实现同步 Dispose 以兼容 using 语句
    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}
