namespace FlexCms.Framework.Services;

public interface IFcmsContextService
{
    Guid? UserId { get; }
    string? Username { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsSuperAdmin { get; }
    string IpAddress { get; }
    string Browser { get; }
    string Os { get; }
}
