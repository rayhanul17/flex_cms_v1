using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Auth;

public class FcmsRole : IdentityRole<Guid>
{
    public override Guid Id { get => base.Id; set => base.Id = value; }

    public FcmsRole() { }
    public FcmsRole(string roleName) : base(roleName) { }

    public List<IdentityRoleClaim<Guid>> Claims { get; set; } = [];
}
