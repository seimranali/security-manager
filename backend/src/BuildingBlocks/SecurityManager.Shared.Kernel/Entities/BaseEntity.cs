namespace SecurityManager.Shared.Kernel.Entities;

/// <summary>Base type for every persisted aggregate / entity across all services.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
