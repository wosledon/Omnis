namespace Omnis.EfCore.Contracts;

public interface ISoftDeleteEntity
{
    public bool IsDeleted { get; set; }
}
