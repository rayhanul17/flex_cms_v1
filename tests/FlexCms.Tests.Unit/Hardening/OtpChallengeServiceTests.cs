using System.Reflection;
using FlexCms.Framework.Auth.TwoFactor;
using Xunit;

namespace FlexCms.Tests.Unit.Hardening;

/// <summary>
/// OTP service is heavy on collaborators (UserManager, EmailService,
/// SmsSender, repository) so the full integration path is exercised in
/// the integration suite. These unit tests cover the pure helpers
/// (code generation entropy, masking) which carry the real safety
/// guarantees.
/// </summary>
public class OtpChallengeServiceTests
{
    // ── 6-digit code generation ──────────────────────────────────────────────

    [Fact]
    public void Generated_otp_is_always_6_digits()
    {
        var gen = typeof(OtpChallengeService).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        for (var i = 0; i < 200; i++)
        {
            var code = (string)gen.Invoke(null, null)!;
            Assert.Equal(6, code.Length);
            Assert.Matches("^[0-9]{6}$", code);
        }
    }

    [Fact]
    public void Generated_otps_are_distinct_across_many_calls()
    {
        // Not a uniqueness guarantee — birthday-paradox-ok at 6 digits, but a
        // run of 200 should comfortably hit ~199 distinct values. A stuck RNG
        // would obviously fail this.
        var gen = typeof(OtpChallengeService).GetMethod("GenerateCode",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++) seen.Add((string)gen.Invoke(null, null)!);
        Assert.True(seen.Count > 180,
            $"Only {seen.Count} distinct codes in 200 calls — RNG looks broken.");
    }

    // ── Recovery code generation ─────────────────────────────────────────────

    [Fact]
    public void Recovery_code_format_is_XXXXX_XXXXX()
    {
        var gen = typeof(OtpChallengeService).GetMethod("GenerateRecoveryCode",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var code = (string)gen.Invoke(null, null)!;
        Assert.Equal(11, code.Length);
        Assert.Equal('-', code[5]);
        // Alphabet excludes ambiguous chars (0, O, 1, I, l) — confirm no leak.
        Assert.DoesNotContain('0', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('1', code);
        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('l', code);
    }

    [Fact]
    public void Recovery_code_normalization_strips_dashes_and_lowercase()
    {
        var norm = typeof(OtpChallengeService).GetMethod("NormalizeRecoveryCode",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal("ABCDEFGHJK", norm.Invoke(null, ["abcde-fghjk"]));
        Assert.Equal("ABCDEFGHJK", norm.Invoke(null, [" ABCDE FGHJK "]));
        Assert.Equal("ABCDEFGHJK", norm.Invoke(null, ["ABCDE-FGHJK"]));
    }

    // ── Masking ──────────────────────────────────────────────────────────────

    [Fact]
    public void Email_masking_keeps_first_and_last_local_chars()
    {
        var m = typeof(OtpChallengeService).GetMethod("MaskEmail",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        // "rayhanul" → 'r' + 6 stars + 'l' + "@example.com"
        Assert.Equal("r******l@example.com", m.Invoke(null, ["rayhanul@example.com"]));
        // "acb" → 'a' + 1 star + 'b' + "@x.com" (max(1, 3-2)=1 star)
        Assert.Equal("a*b@x.com", m.Invoke(null, ["acb@x.com"]));
    }

    [Fact]
    public void Email_masking_safely_handles_short_local()
    {
        var m = typeof(OtpChallengeService).GetMethod("MaskEmail",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        // Single-letter local-part: just return as-is — masking would hide
        // everything anyway and the destination is already minimal info.
        Assert.Equal("a@x.com", m.Invoke(null, ["a@x.com"]));
    }

    [Fact]
    public void Phone_masking_shows_first_2_and_last_3_for_BD_numbers()
    {
        var m = typeof(OtpChallengeService).GetMethod("MaskPhone",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        // 11-digit BD mobile: "01712345678" → "01******678" (first 2 + last 3 + 6 stars).
        Assert.Equal("01******678", m.Invoke(null, ["01712345678"]));
    }
}
