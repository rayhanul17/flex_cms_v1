namespace FlexCms.Framework.Db.Migration;

public interface IFcmsMigrationCoordinator
{
    Task<bool> TryAcquireLockAsync(string resource, TimeSpan timeout, CancellationToken ct = default);
    Task ReleaseLockAsync(string resource, CancellationToken ct = default);
}
