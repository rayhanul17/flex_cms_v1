using Microsoft.AspNetCore.Identity;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FlexCms.Framework.Auth;

public class FcmsRole : IdentityRole<Guid>
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public override Guid Id { get => base.Id; set => base.Id = value; }

    public FcmsRole() { }
    public FcmsRole(string roleName) : base(roleName) { }
}
