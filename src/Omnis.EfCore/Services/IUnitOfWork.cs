namespace Omnis.EfCore.Services;

public interface IUnitOfWork
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
}
