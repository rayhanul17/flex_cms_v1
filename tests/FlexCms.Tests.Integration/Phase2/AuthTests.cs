using FlexCms.Framework.Auth;
using FlexCms.Framework.Auth.Ef;
using FlexCms.Framework.Db.Ef;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Integration.Phase2;

/// <summary>
/// Auth tests using EF InMemory — no Docker required.
/// Covers: Register, Login, Lockout, ForcePasswordChange flag, password reset tokens.
/// Uses a real DI container so AddDefaultTokenProviders and validators are wired correctly.
/// </summary>
public class AuthTests : IDisposable
{
    private readonly ServiceProvider _sp;
    private readonly FcmsDbContext _db;
    private readonly UserManager<FcmsUser> _userManager;

    public AuthTests()
    {
        var opts = new DbContextOptionsBuilder<FcmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(opts);
        services.AddScoped<FcmsDbContext>(sp => new FcmsDbContext(sp.GetRequiredService<DbContextOptions<FcmsDbContext>>()));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<FcmsDbContext>());
        services.AddLogging();
        services.AddDataProtection();

        services.AddIdentityCore<FcmsUser>(o =>
        {
            o.Password.RequireDigit = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireUppercase = true;
            o.Password.RequireNonAlphanumeric = true;
            o.Password.RequiredLength = 8;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.AllowedForNewUsers = true;
            o.User.RequireUniqueEmail = true;
        })
        .AddRoles<FcmsRole>()
        .AddDefaultTokenProviders()
        .AddEntityFrameworkStores<FcmsDbContext>();

        _sp = services.BuildServiceProvider();
        _db = _sp.GetRequiredService<FcmsDbContext>();
        _userManager = _sp.GetRequiredService<UserManager<FcmsUser>>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _sp.Dispose();
    }

    // ── Register ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_valid_user_succeeds()
    {
        var user = new FcmsUser { UserName = "testuser", Email = "test@example.com" };
        var result = await _userManager.CreateAsync(user, "Test@1234");

        Assert.True(result.Succeeded);
        Assert.NotEqual(Guid.Empty, user.Id);
    }

    [Fact]
    public async Task Register_duplicate_email_fails()
    {
        var user1 = new FcmsUser { UserName = "user1", Email = "dup@example.com" };
        await _userManager.CreateAsync(user1, "Test@1234");

        var user2 = new FcmsUser { UserName = "user2", Email = "dup@example.com" };
        var result = await _userManager.CreateAsync(user2, "Test@1234");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task Register_weak_password_fails()
    {
        var user = new FcmsUser { UserName = "weakuser", Email = "weak@example.com" };
        var result = await _userManager.CreateAsync(user, "password");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Register_new_user_has_ForcePasswordChange_false_by_default()
    {
        var user = new FcmsUser { UserName = "newuser", Email = "newuser@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        var loaded = await _userManager.FindByEmailAsync("newuser@example.com");
        Assert.NotNull(loaded);
        Assert.False(loaded.ForcePasswordChange);
    }

    [Fact]
    public async Task Register_ForcePasswordChange_can_be_set_to_true()
    {
        var user = new FcmsUser { UserName = "forced", Email = "forced@example.com", ForcePasswordChange = true };
        await _userManager.CreateAsync(user, "Test@1234");

        var loaded = await _userManager.FindByEmailAsync("forced@example.com");
        Assert.NotNull(loaded);
        Assert.True(loaded.ForcePasswordChange);
    }

    // ── Password verify ───────────────────────────────────────────────────────

    [Fact]
    public async Task CheckPassword_correct_password_returns_true()
    {
        var user = new FcmsUser { UserName = "checkpwd", Email = "checkpwd@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        Assert.True(await _userManager.CheckPasswordAsync(user, "Test@1234"));
    }

    [Fact]
    public async Task CheckPassword_wrong_password_returns_false()
    {
        var user = new FcmsUser { UserName = "wrongpwd", Email = "wrongpwd@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        Assert.False(await _userManager.CheckPasswordAsync(user, "WrongPass!9"));
    }

    // ── Lockout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lockout_after_max_failed_attempts()
    {
        var user = new FcmsUser { UserName = "lockout", Email = "lockout@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        for (int i = 0; i < 5; i++)
            await _userManager.AccessFailedAsync(user);

        Assert.True(await _userManager.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Lockout_reset_after_calling_ResetAccessFailedCount()
    {
        var user = new FcmsUser { UserName = "lockrst", Email = "lockrst@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        await _userManager.AccessFailedAsync(user);
        await _userManager.AccessFailedAsync(user);
        await _userManager.ResetAccessFailedCountAsync(user);

        Assert.Equal(0, await _userManager.GetAccessFailedCountAsync(user));
    }

    // ── Password reset token ──────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePasswordResetToken_returns_non_empty_token()
    {
        var user = new FcmsUser { UserName = "resetpwd", Email = "resetpwd@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        Assert.NotEmpty(token);
    }

    [Fact]
    public async Task ResetPassword_with_valid_token_succeeds()
    {
        var user = new FcmsUser { UserName = "resetok", Email = "resetok@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, "NewPass@9999");

        Assert.True(result.Succeeded);
        Assert.True(await _userManager.CheckPasswordAsync(user, "NewPass@9999"));
    }

    [Fact]
    public async Task ResetPassword_with_invalid_token_fails()
    {
        var user = new FcmsUser { UserName = "resetbad", Email = "resetbad@example.com" };
        await _userManager.CreateAsync(user, "Test@1234");

        var result = await _userManager.ResetPasswordAsync(user, "invalid-token", "NewPass@9999");
        Assert.False(result.Succeeded);
    }
}
