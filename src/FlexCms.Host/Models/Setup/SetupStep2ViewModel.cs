using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Setup;

public class SetupStep2ViewModel
{
    [Required, MaxLength(200)]
    public string SiteName { get; set; } = "";

    [MaxLength(400)]
    public string Tagline { get; set; } = "";

    [MaxLength(500)]
    public string BaseUrl { get; set; } = "";

    [Required]
    public string TimeZoneId { get; set; } = "Asia/Dhaka";

    [Required]
    public string DefaultLanguage { get; set; } = "en";
}
