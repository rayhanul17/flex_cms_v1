using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Setup;

public class SetupStep3ViewModel
{
    [Required, MaxLength(200)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MinLength(8), MaxLength(100)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = "";
}
