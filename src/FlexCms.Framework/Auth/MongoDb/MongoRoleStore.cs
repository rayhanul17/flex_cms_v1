using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace FlexCms.Framework.Auth.MongoDb;

public class MongoRoleStore : IRoleStore<FcmsRole>
{
    private readonly IMongoCollection<FcmsRole> _roles;

    public MongoRoleStore(IMongoDatabase database)
    {
        _roles = database.GetCollection<FcmsRole>("fcmsroles");
    }

    private FilterDefinition<FcmsRole> ById(string roleId) =>
        Builders<FcmsRole>.Filter.Eq(r => r.Id, Guid.Parse(roleId));

    public async Task<IdentityResult> CreateAsync(FcmsRole role, CancellationToken ct)
    {
        await _roles.InsertOneAsync(role, null, ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(FcmsRole role, CancellationToken ct)
    {
        await _roles.ReplaceOneAsync(ById(role.Id.ToString()), role, new ReplaceOptions(), ct);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(FcmsRole role, CancellationToken ct)
    {
        await _roles.DeleteOneAsync(ById(role.Id.ToString()), ct);
        return IdentityResult.Success;
    }

    public Task<string> GetRoleIdAsync(FcmsRole role, CancellationToken ct) =>
        Task.FromResult(role.Id.ToString());

    public Task<string?> GetRoleNameAsync(FcmsRole role, CancellationToken ct) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(FcmsRole role, string? roleName, CancellationToken ct)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(FcmsRole role, CancellationToken ct) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(FcmsRole role, string? normalizedName, CancellationToken ct)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<FcmsRole?> FindByIdAsync(string roleId, CancellationToken ct)
        => await _roles.Find(ById(roleId)).FirstOrDefaultAsync(ct);

    public async Task<FcmsRole?> FindByNameAsync(string normalizedRoleName, CancellationToken ct)
        => await _roles.Find(Builders<FcmsRole>.Filter.Eq(r => r.NormalizedName, normalizedRoleName))
                       .FirstOrDefaultAsync(ct);

    public void Dispose() { }
}
