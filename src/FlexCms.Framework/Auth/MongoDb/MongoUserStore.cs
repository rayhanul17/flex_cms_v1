using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace FlexCms.Framework.Auth.MongoDb;

public class MongoUserStore :
    IUserStore<FcmsUser>,
    IUserPasswordStore<FcmsUser>,
    IUserEmailStore<FcmsUser>,
    IUserLockoutStore<FcmsUser>,
    IUserRoleStore<FcmsUser>,
    IUserSecurityStampStore<FcmsUser>,
    IQueryableUserStore<FcmsUser>,
    IUserClaimStore<FcmsUser>,
    IUserLoginStore<FcmsUser>,
    IUserTwoFactorStore<FcmsUser>,
    IUserPhoneNumberStore<FcmsUser>,
    IUserAuthenticationTokenStore<FcmsUser>
{
    private readonly IMongoCollection<FcmsUser> _users;
    private readonly IMongoCollection<FcmsRole> _roles;

    public MongoUserStore(IMongoDatabase database)
    {
        _users = database.GetCollection<FcmsUser>(Helpers.FcmsHelper.GetTableName<FcmsUser>("fcms"));
        _roles = database.GetCollection<FcmsRole>(Helpers.FcmsHelper.GetTableName<FcmsRole>("fcms"));
    }

    public IQueryable<FcmsUser> Users => _users.AsQueryable();

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
        // Optimistic concurrency on the Identity-managed ConcurrencyStamp
        // (same field EF Identity stores in IsConcurrencyToken). Two admins
        // both calling AddToRoleAsync on the same user used to silently lose
        // one of the writes; now the second write fails with a conflict and
        // UserManager surfaces the standard Identity "ConcurrencyFailure"
        // result. Caller can refetch + retry.
        var oldStamp = user.ConcurrencyStamp;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = FlexCms.Framework.Clock.FcmsTime.Now;

        var filter = Builders<FcmsUser>.Filter.And(
            ById(user.Id.ToString()),
            string.IsNullOrEmpty(oldStamp)
                ? Builders<FcmsUser>.Filter.Or(
                    Builders<FcmsUser>.Filter.Exists(u => u.ConcurrencyStamp, false),
                    Builders<FcmsUser>.Filter.Eq(u => u.ConcurrencyStamp, (string?)null),
                    Builders<FcmsUser>.Filter.Eq(u => u.ConcurrencyStamp, ""))
                : Builders<FcmsUser>.Filter.Eq(u => u.ConcurrencyStamp, oldStamp));

        var result = await _users.ReplaceOneAsync(filter, user, new ReplaceOptions(), ct);
        if (result.MatchedCount == 0)
        {
            // Restore the stamp so the caller's user object reflects what
            // was attempted (and can be diff'd against the latest fetch).
            user.ConcurrencyStamp = oldStamp;
            return IdentityResult.Failed(new IdentityError
            {
                Code = "ConcurrencyFailure",
                Description = "Optimistic concurrency failure, object has been modified.",
            });
        }
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
    public async Task AddToRoleAsync(FcmsUser user, string roleName, CancellationToken ct)
    {
        // UserManager passes the normalized (uppercase) role name.
        // Look up the actual role name so the claim stored in the cookie matches
        // the original casing expected by IsInRole("SuperAdmin") etc.
        var role = await _roles.Find(
            Builders<FcmsRole>.Filter.Eq(r => r.NormalizedName, roleName.ToUpperInvariant()))
            .FirstOrDefaultAsync(ct);
        var nameToStore = role?.Name ?? roleName;
        if (!user.Roles.Contains(nameToStore, StringComparer.OrdinalIgnoreCase))
            user.Roles.Add(nameToStore);
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
        // roleName may be normalized (uppercase). Look up the actual stored name first.
        var role = await _roles.Find(
            Builders<FcmsRole>.Filter.Eq(r => r.NormalizedName, roleName.ToUpperInvariant()))
            .FirstOrDefaultAsync(ct);
        var nameToSearch = role?.Name ?? roleName;
        var filter = Builders<FcmsUser>.Filter.AnyEq(u => u.Roles, nameToSearch);
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

    // IUserClaimStore
    public Task<IList<System.Security.Claims.Claim>> GetClaimsAsync(FcmsUser user, CancellationToken ct)
        => Task.FromResult<IList<System.Security.Claims.Claim>>(user.Claims.Select(c => c.ToClaim()).ToList());

    public Task AddClaimsAsync(FcmsUser user, IEnumerable<System.Security.Claims.Claim> claims, CancellationToken ct)
    {
        foreach (var claim in claims)
            user.Claims.Add(new IdentityUserClaim<Guid> { UserId = user.Id, ClaimType = claim.Type, ClaimValue = claim.Value });
        return Task.CompletedTask;
    }

    public Task ReplaceClaimAsync(FcmsUser user, System.Security.Claims.Claim claim, System.Security.Claims.Claim newClaim, CancellationToken ct)
    {
        var existing = user.Claims.FirstOrDefault(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);
        if (existing is not null)
        {
            existing.ClaimType = newClaim.Type;
            existing.ClaimValue = newClaim.Value;
        }
        return Task.CompletedTask;
    }

    public Task RemoveClaimsAsync(FcmsUser user, IEnumerable<System.Security.Claims.Claim> claims, CancellationToken ct)
    {
        foreach (var claim in claims)
        {
            var existing = user.Claims.FirstOrDefault(c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);
            if (existing is not null) user.Claims.Remove(existing);
        }
        return Task.CompletedTask;
    }

    public async Task<IList<FcmsUser>> GetUsersForClaimAsync(System.Security.Claims.Claim claim, CancellationToken ct)
    {
        var filter = Builders<FcmsUser>.Filter.ElemMatch(u => u.Claims, c => c.ClaimType == claim.Type && c.ClaimValue == claim.Value);
        return await _users.Find(filter).ToListAsync(ct);
    }

    // IUserLoginStore
    public Task AddLoginAsync(FcmsUser user, UserLoginInfo login, CancellationToken ct)
    {
        user.Logins.Add(new IdentityUserLogin<Guid> { UserId = user.Id, LoginProvider = login.LoginProvider, ProviderKey = login.ProviderKey, ProviderDisplayName = login.ProviderDisplayName });
        return Task.CompletedTask;
    }

    public Task RemoveLoginAsync(FcmsUser user, string loginProvider, string providerKey, CancellationToken ct)
    {
        var existing = user.Logins.FirstOrDefault(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
        if (existing is not null) user.Logins.Remove(existing);
        return Task.CompletedTask;
    }

    public Task<IList<UserLoginInfo>> GetLoginsAsync(FcmsUser user, CancellationToken ct)
        => Task.FromResult<IList<UserLoginInfo>>(user.Logins.Select(l => new UserLoginInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName)).ToList());

    public async Task<FcmsUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken ct)
    {
        var filter = Builders<FcmsUser>.Filter.ElemMatch(u => u.Logins, l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
        return await _users.Find(filter).FirstOrDefaultAsync(ct);
    }

    // IUserTwoFactorStore
    public Task SetTwoFactorEnabledAsync(FcmsUser user, bool enabled, CancellationToken ct)
    {
        user.TwoFactorEnabled = enabled;
        return Task.CompletedTask;
    }

    public Task<bool> GetTwoFactorEnabledAsync(FcmsUser user, CancellationToken ct)
        => Task.FromResult(user.TwoFactorEnabled);

    // IUserPhoneNumberStore
    public Task SetPhoneNumberAsync(FcmsUser user, string? phoneNumber, CancellationToken ct)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<string?> GetPhoneNumberAsync(FcmsUser user, CancellationToken ct)
        => Task.FromResult(user.PhoneNumber);

    public Task<bool> GetPhoneNumberConfirmedAsync(FcmsUser user, CancellationToken ct)
        => Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(FcmsUser user, bool confirmed, CancellationToken ct)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // IUserAuthenticationTokenStore
    public Task SetTokenAsync(FcmsUser user, string loginProvider, string name, string? value, CancellationToken ct)
    {
        var existing = user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name);
        if (existing is not null)
        {
            existing.Value = value;
        }
        else
        {
            user.Tokens.Add(new IdentityUserToken<Guid> { UserId = user.Id, LoginProvider = loginProvider, Name = name, Value = value });
        }
        return Task.CompletedTask;
    }

    public Task RemoveTokenAsync(FcmsUser user, string loginProvider, string name, CancellationToken ct)
    {
        var existing = user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name);
        if (existing is not null) user.Tokens.Remove(existing);
        return Task.CompletedTask;
    }

    public Task<string?> GetTokenAsync(FcmsUser user, string loginProvider, string name, CancellationToken ct)
        => Task.FromResult(user.Tokens.FirstOrDefault(t => t.LoginProvider == loginProvider && t.Name == name)?.Value);

    public void Dispose() { }
}
