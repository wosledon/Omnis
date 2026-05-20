namespace Omnis.EfCore.Services;

/// <summary>
/// 审计上下文提供者接口，用于获取当前用户的相关信息，如用户Id和用户名。
/// </summary>
public interface IAuditContextProvider
{
    /// <summary>
    /// 获取当前用户的用户Id
    /// </summary>
    /// <returns></returns>
    Guid GetCurrentUserId();

    /// <summary>
    /// 获取当前用户的用户名
    /// </summary>
    /// <returns></returns>
    string? GetCurrentUserName();
}
