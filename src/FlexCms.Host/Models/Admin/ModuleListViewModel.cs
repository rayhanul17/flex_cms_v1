namespace FlexCms.Host.Models.Admin;

public class ModuleListViewModel
{
    public List<ModuleListItem> Modules { get; set; } = [];
}

public class ModuleListItem
{
    public string ModuleId { get; set; } = "";
    public string ModuleName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string TablePrefix { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? ActivatedAt { get; set; }
    public string[] DependsOn { get; set; } = [];
}
