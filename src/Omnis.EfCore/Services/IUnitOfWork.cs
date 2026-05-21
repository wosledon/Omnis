using Omnis.EfCore.Contracts;

namespace Omnis.EfCore.Services;

public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// 获取指定类型的查询对象
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <returns>查询对象</returns>
    IQueryable<T> Q<T>() where T : class;

    /// <summary>
    /// 提交当前单元操作，保存更改到数据库
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>提交结果，成功返回true，失败返回false</returns>
    ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取事务管理器
    /// </summary>
    ITransactionManager TransactionManager { get; }

    /// <summary>
    /// 是否存在活动事务
    /// </summary>
    bool HasActiveTransaction { get; }

    /// <summary>
    /// 开始新事务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>事务上下文，使用后需 disposing（自动回滚除非已提交）</returns>
    ValueTask<TransactionContext> BeginTransactionAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交事务
    /// </summary>
    ValueTask CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回滚事务
    /// </summary>
    ValueTask RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
