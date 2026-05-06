using System.ComponentModel.DataAnnotations;
using FlexCms.Framework.Messaging;

namespace FlexCms.Host.Models.Admin;

public class BroadcastViewModel
{
    [Display(Name = "Channel")]
    public MessageChannel Channel { get; set; } = MessageChannel.Email;

    [Display(Name = "Send to")]
    public BroadcastTarget Target { get; set; } = BroadcastTarget.AllUsers;

    [Display(Name = "Role")]
    public string? RoleName { get; set; }

    [Display(Name = "Selected user IDs (comma-separated)")]
    public List<Guid> SelectedUserIds { get; set; } = [];

    [Display(Name = "Subject (email only)")]
    public string? Subject { get; set; }

    [Display(Name = "Message body")]
    [Required]
    public string Body { get; set; } = "";

    [Display(Name = "HTML body")]
    public bool IsHtml { get; set; } = true;

    public List<string> AvailableRoles { get; set; } = [];
}
