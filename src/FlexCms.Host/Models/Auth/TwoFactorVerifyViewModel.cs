using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Auth;

public class TwoFactorVerifyViewModel
{
    /// <summary>6-digit OTP OR a recovery code formatted as XXXXX-XXXXX.</summary>
    [Required(ErrorMessage = "Enter the code we sent (or a recovery code).")]
    [StringLength(20)]
    public string? Code { get; set; }
}
