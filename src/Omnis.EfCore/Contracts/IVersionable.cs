namespace Omnis.EfCore.Contracts;

public interface IVersionable
{
    public byte[] RowVersion { get; set; }
}
