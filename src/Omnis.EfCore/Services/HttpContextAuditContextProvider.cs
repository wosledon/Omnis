namespace Omnis.EfCore.Services;

/// <inheritdoc/>
public class HttpContextAuditContextProvider
    : IAuditContextProvider
{
    /// <inheritdoc/>
    public Guid GetCurrentUserId()
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public string? GetCurrentUserName()
    {
        throw new NotImplementedException();
    }
}
