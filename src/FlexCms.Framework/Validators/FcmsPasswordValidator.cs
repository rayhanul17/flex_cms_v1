using FlexCms.Framework.Auth;
using Microsoft.AspNetCore.Identity;

namespace FlexCms.Framework.Validators;

public class FcmsPasswordValidator : IPasswordValidator<FcmsUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<FcmsUser> manager, FcmsUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "EmptyPassword", Description = "Password cannot be empty." }));

        var errors = new List<IdentityError>();

        if (password.Length < 8)
            errors.Add(new IdentityError { Code = "PasswordTooShort", Description = "Password must be at least 8 characters." });

        if (!password.Any(char.IsUpper))
            errors.Add(new IdentityError { Code = "PasswordRequiresUpper", Description = "Password must contain at least one uppercase letter." });

        if (!password.Any(char.IsLower))
            errors.Add(new IdentityError { Code = "PasswordRequiresLower", Description = "Password must contain at least one lowercase letter." });

        if (!password.Any(char.IsDigit))
            errors.Add(new IdentityError { Code = "PasswordRequiresDigit", Description = "Password must contain at least one digit." });

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add(new IdentityError { Code = "PasswordRequiresSpecial", Description = "Password must contain at least one special character." });

        return Task.FromResult(errors.Count > 0
            ? IdentityResult.Failed([.. errors])
            : IdentityResult.Success);
    }
}
