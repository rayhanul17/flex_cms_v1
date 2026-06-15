using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Admin;

public class ModuleListViewModel
{
    public List<ModuleListItem> Modules { get; set; } = [];
}

public class ScaffoldModuleViewModel
{
    [Required, RegularExpression(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$",
        ErrorMessage = "Must be a dot-separated identifier, e.g. FlexCms.Blog")]
    public string ModuleId { get; set; } = "";

    [Required, RegularExpression(@"^[a-z][a-z0-9_]*$",
        ErrorMessage = "Lowercase letters, digits, underscores only.")]
    public string TablePrefix { get; set; } = "";
}

public class ModuleListItem
{
    public string ModuleId { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string TablePrefix { get; set; } = "";
    public string MinFrameworkVersion { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastActivationAttemptAt { get; set; }
    public string? ActivationError { get; set; }
    public string[] DependsOn { get; set; } = [];
    public int RequestedPermissionsCount { get; set; }
}
