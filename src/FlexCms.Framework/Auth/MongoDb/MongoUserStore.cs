using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace FlexCms.Framework.Auth.MongoDb;

public class MongoUserStore :
    IUserStore<FcmsUser>,
    IUserPasswordStore<FcmsUser>,
    IUserEmailStore<FcmsUser>,
    IUserLockoutStore<FcmsUser>,
    IUserRoleStore<FcmsUser>,
    IUserSecurityStampStore<FcmsUser>
{
    private readonly IMongoCollection<FcmsUser> _users;

    public MongoUserStore(IMongoDatabase database)
    {
        _users = database.GetCollection<FcmsUser>("fcmsusers");
    }

    private FilterDefinition<FcmsUser> ById(string userId) =>
        Builders<FcmsUser>.Filter.Eq(u => u.Id, Guid.Parse(userId));

    // IUserStore
    public async Task<IdentityResult> CreateAsync(FcmsUser user, CancellationToken ct)
    {
        await _users.InsertOneAsync(user, null, ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(FcmsUser user, CancellationToken ct)
    {
        await _users.ReplaceOneAsync(ById(user.Id.ToString()), user, new ReplaceOptions(), ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(FcmsUser user, CancellationToken ct)
    {
        await _users.DeleteOneAsync(ById(user.Id.ToString()), ct);
        return IdentityResult.Success;
    }

    public async Task<FcmsUser?> FindByIdAsync(string userId, CancellationToken ct)
        => await _users.Find(ById(userId)).FirstOrDefaultAsync(ct);

    public async Task<FcmsUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct)
        => await _users.Find(Builders<FcmsUser>.Filter.Eq(u => u.NormalizedUserName, normalizedUserName))
                       .FirstOrDefaultAsync(ct);

    public Task<string> GetUserIdAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(FcmsUser user, string? userName, CancellationToken ct)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(FcmsUser user, string? normalizedName, CancellationToken ct)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    // IUserPasswordStore
    public Task SetPasswordHashAsync(FcmsUser user, string? passwordHash, CancellationToken ct)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // IUserEmailStore
    public Task SetEmailAsync(FcmsUser user, string? email, CancellationToken ct)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(FcmsUser user, bool confirmed, CancellationToken ct)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public async Task<FcmsUser?> FindByEmailAsync(string normalizedEmail, CancellationToken ct)
        => await _users.Find(Builders<FcmsUser>.Filter.Eq(u => u.NormalizedEmail, normalizedEmail))
                       .FirstOrDefaultAsync(ct);

    public Task<string?> GetNormalizedEmailAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(FcmsUser user, string? normalizedEmail, CancellationToken ct)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // IUserLockoutStore
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.LockoutEnd);

    public Task SetLockoutEndDateAsync(FcmsUser user, DateTimeOffset? lockoutEnd, CancellationToken ct)
    {
        user.LockoutEnd = lockoutEnd;
        return Task.CompletedTask;
    }

    public Task<int> IncrementAccessFailedCountAsync(FcmsUser user, CancellationToken ct)
    {
        user.AccessFailedCount++;
        return Task.FromResult(user.AccessFailedCount);
    }

    public Task ResetAccessFailedCountAsync(FcmsUser user, CancellationToken ct)
    {
        user.AccessFailedCount = 0;
        return Task.CompletedTask;
    }

    public Task<int> GetAccessFailedCountAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.AccessFailedCount);

    public Task<bool> GetLockoutEnabledAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.LockoutEnabled);

    public Task SetLockoutEnabledAsync(FcmsUser user, bool enabled, CancellationToken ct)
    {
        user.LockoutEnabled = enabled;
        return Task.CompletedTask;
    }

    // IUserRoleStore
    public Task AddToRoleAsync(FcmsUser user, string roleName, CancellationToken ct)
    {
        if (!user.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase))
            user.Roles.Add(roleName);
        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(FcmsUser user, string roleName, CancellationToken ct)
    {
        var existing = user.Roles.FirstOrDefault(r =>
            string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) user.Roles.Remove(existing);
        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult<IList<string>>(user.Roles);

    public Task<bool> IsInRoleAsync(FcmsUser user, string roleName, CancellationToken ct) =>
        Task.FromResult(user.Roles.Contains(roleName, StringComparer.OrdinalIgnoreCase));

    public async Task<IList<FcmsUser>> GetUsersInRoleAsync(string roleName, CancellationToken ct)
    {
        var filter = Builders<FcmsUser>.Filter.AnyEq(u => u.Roles, roleName);
        return await _users.Find(filter).ToListAsync(ct);
    }

    // IUserSecurityStampStore
    public Task SetSecurityStampAsync(FcmsUser user, string stamp, CancellationToken ct)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(FcmsUser user, CancellationToken ct) =>
        Task.FromResult(user.SecurityStamp);

    public void Dispose() { }
}
