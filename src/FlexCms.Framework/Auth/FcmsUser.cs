using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Auth;

public class FcmsUser : IdentityUser<Guid>
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public override Guid Id { get => base.Id; set => base.Id = value; }

    public bool ForcePasswordChange { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<string> Roles { get; set; } = [];
}
