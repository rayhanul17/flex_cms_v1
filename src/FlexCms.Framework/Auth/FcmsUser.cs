using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Auth;

public class FcmsUser : IdentityUser<Guid>
{
    public override Guid Id { get => base.Id; set => base.Id = value; }

    public bool ForcePasswordChange { get; set; }
    public DateTime CreatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public DateTime UpdatedAt { get; set; } = FlexCms.Framework.Clock.FcmsTime.Now;
    public List<string> Roles { get; set; } = [];

    // Embedded collections for MongoDB
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];
}
