using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Auth;

public class VerifyOtpViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 4)]
    public string Otp { get; set; } = string.Empty;
}
