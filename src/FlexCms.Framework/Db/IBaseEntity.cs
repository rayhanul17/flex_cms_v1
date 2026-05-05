namespace FlexCms.Framework.Db;

public interface IBaseEntity
{
    Guid Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
    Guid? CreatedBy { get; set; }
    Guid? UpdatedBy { get; set; }

    /// <summary>
    /// Lifecycle status. <see cref="EntityStatus.Active"/> by default.
    /// Soft-delete sets this to <see cref="EntityStatus.Deleted"/>.
    /// </summary>
    EntityStatus Status { get; set; }

    DateTime? DeletedAt { get; set; }
}
