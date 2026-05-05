namespace FlexCms.Framework.Db.Ef;

public abstract class BaseEfEntity : IBaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public DateTime UpdatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public DateTime? DeletedAt { get; set; }
}
