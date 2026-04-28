using System.ComponentModel.DataAnnotations;

namespace FlexCms.Host.Models.Setup;

public class SetupStep3ViewModel
{
    [Required, MaxLength(200)]
    public string DisplayName { get; set; } = "";

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MinLength(8), MaxLength(100)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}
