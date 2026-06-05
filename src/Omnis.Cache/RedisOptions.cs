namespace Omnis.Cache;

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int Database { get; set; }
}
