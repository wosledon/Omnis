using System.Data;

namespace Omnis.EfCore.Contracts;

/// <summary>
/// 事务管理器，支持事务嵌套
/// </summary>
public interface ITransactionManager : IAsyncDisposable
{
    /// <summary>
    /// 是否存在活动事务
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// 获取当前活动的事务上下文
    /// </summary>
    TransactionContext? ActiveTransaction { get; }

    /// <summary>
    /// 开始新的事务
    /// </summary>
    /// <param name="isolationLevel">隔离级别</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>事务上下文， disposing 时会自动回滚（除非已经提交）</returns>
    ValueTask<TransactionContext> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交当前事务
    /// </summary>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚当前事务
    /// </summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}
