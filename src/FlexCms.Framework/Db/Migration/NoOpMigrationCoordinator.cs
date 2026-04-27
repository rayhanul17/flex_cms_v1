namespace FlexCms.Framework.Db.Migration;

// Default for single-instance deploy — no distributed locking needed
public sealed class NoOpMigrationCoordinator : IFcmsMigrationCoordinator
{
    public Task<bool> TryAcquireLockAsync(string resource, TimeSpan timeout, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task ReleaseLockAsync(string resource, CancellationToken ct = default)
        => Task.CompletedTask;
}
