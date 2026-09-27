namespace Entities.Base;

/// <summary>Base class for every table: Guid v7 key (sortable) + audit dates.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Entities that are never physically deleted (filtered out by a global query filter).</summary>
public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
}
