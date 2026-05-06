using System.Security.Claims;
using FlexCms.Framework.Db;
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

    /// <summary>
    /// User lifecycle status. Source of truth for the admin UI active/deactive toggle.
    /// Auth-time blocking is enforced via Identity's <c>LockoutEnd</c>; the controller
    /// keeps the two in sync (Active ⇔ LockoutEnd null/past).
    /// MongoDB: stored as <see cref="MongoDB.Bson.BsonType.Int32"/>.
    /// </summary>
    [BsonRepresentation(BsonType.Int32)]
    public EntityStatus Status { get; set; } = EntityStatus.Active;

    public List<string> Roles { get; set; } = [];

    // Embedded collections for MongoDB
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];
}
