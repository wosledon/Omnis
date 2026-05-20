namespace Omnis.EfCore.Contracts;

public class EntityBase : IAuditableEntity, ISoftDeleteEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
}