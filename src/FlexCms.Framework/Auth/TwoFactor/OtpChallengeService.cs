using System.Security.Cryptography;
using System.Text;
using FlexCms.Framework.Clock;
using FlexCms.Framework.Cms;
using FlexCms.Framework.Db;
using FlexCms.Framework.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Auth.TwoFactor;

public sealed class OtpChallengeService : IOtpChallengeService
{
    /// <summary>5-min expiry: long enough for SMS carrier delays, short enough to keep replay risk small.</summary>
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    /// <summary>5 attempts then the OTP is invalidated — user must request a new one.</summary>
    public const int MaxAttempts = 5;

    private readonly UserManager<FcmsUser> _userManager;
    private readonly IFcmsEmailService _email;
    private readonly IFcmsSmsSender _sms;
    private readonly IRepository<FcmsRecoveryCode> _recovery;
    private readonly IFcmsUnitOfWork _uow;
    private readonly IFcmsLogService _audit;
    private readonly ILogger<OtpChallengeService> _logger;

    public OtpChallengeService(
        UserManager<FcmsUser> userManager,
        IFcmsEmailService email,
        IFcmsSmsSender sms,
        IRepository<FcmsRecoveryCode> recovery,
        IFcmsUnitOfWork uow,
        IFcmsLogService audit,
        ILogger<OtpChallengeService> logger)
    {
        _userManager = userManager;
        _email = email;
        _sms = sms;
        _recovery = recovery;
        _uow = uow;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OtpIssueResult> IssueAsync(FcmsUser user, CancellationToken ct = default)
    {
        if (user is null) return new OtpIssueResult(false, Error: "User required.");
        if (user.TwoFactorChannel == TwoFactorChannel.Disabled)
            return new OtpIssueResult(false, Error: "2FA is not enabled for this account.");

        var code = GenerateCode();
        user.PendingOtpHash = HashCode(code);
        user.PendingOtpExpiresAt = FcmsTime.Now.Add(OtpLifetime);
        user.PendingOtpAttempts = 0;
        await _userManager.UpdateAsync(user);

        // Channel selection. Falls back to email if the user's chosen channel
        // isn't deliverable (no phone for SMS, no email confirmed) — admin
        // can always disable 2FA for a stuck user from the user-edit screen.
        if (user.TwoFactorChannel == TwoFactorChannel.Sms && !string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            var smsResult = await _sms.SendAsync(new SmsMessage(user.PhoneNumber, $"FlexCMS code: {code}. Expires in 5 min."), ct);
            if (!smsResult.Success)
            {
                _logger.LogWarning("OTP SMS send failed for user {UserId}: {Error}", user.Id, smsResult.Error);
                await ClearPendingOtpAsync(user);
                await _audit.LogAsync(FcmsAuditActions.OtpSendFailed, nameof(FcmsUser), user.Id.ToString(),
                    value: new { channel = "SMS", error = smsResult.Error },
                    severity: FcmsLogSeverity.Warning, ct: ct);
                return new OtpIssueResult(false, Error: "Could not send code via SMS. Try again or contact admin.");
            }
            await _audit.LogAsync(FcmsAuditActions.OtpIssued, nameof(FcmsUser), user.Id.ToString(),
                value: new { channel = "SMS", destination = MaskPhone(user.PhoneNumber) }, ct: ct);
            return new OtpIssueResult(true, MaskedDestination: MaskPhone(user.PhoneNumber));
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            await ClearPendingOtpAsync(user);
            return new OtpIssueResult(false, Error: "No deliverable channel — set an email address first.");
        }

        var html = $"""
            <p>Your FlexCMS sign-in code is:</p>
            <p style="font-size:24px;letter-spacing:4px;font-weight:bold">{code}</p>
            <p>The code expires in 5 minutes. If you didn't try to sign in, you can ignore this email.</p>
            """;
        var emailResult = await _email.SendAsync(new EmailMessage(user.Email, "Your FlexCMS sign-in code", html), ct);
        if (!emailResult.Success)
        {
            _logger.LogWarning("OTP email send failed for user {UserId}: {Error}", user.Id, emailResult.Error);
            await ClearPendingOtpAsync(user);
            await _audit.LogAsync(FcmsAuditActions.OtpSendFailed, nameof(FcmsUser), user.Id.ToString(),
                value: new { channel = "Email", error = emailResult.Error },
                severity: FcmsLogSeverity.Warning, ct: ct);
            return new OtpIssueResult(false, Error: "Could not send code via email. Try again or contact admin.");
        }
        await _audit.LogAsync(FcmsAuditActions.OtpIssued, nameof(FcmsUser), user.Id.ToString(),
            value: new { channel = "Email", destination = MaskEmail(user.Email) }, ct: ct);
        return new OtpIssueResult(true, MaskedDestination: MaskEmail(user.Email));
    }

    public async Task<OtpVerifyResult> VerifyAsync(FcmsUser user, string code, CancellationToken ct = default)
    {
        if (user is null || string.IsNullOrEmpty(user.PendingOtpHash)) return OtpVerifyResult.NoPending;
        if (user.PendingOtpExpiresAt is null || user.PendingOtpExpiresAt.Value <= FcmsTime.Now)
            return OtpVerifyResult.Expired;
        if (user.PendingOtpAttempts >= MaxAttempts) return OtpVerifyResult.TooManyAttempts;
        if (string.IsNullOrWhiteSpace(code)) return OtpVerifyResult.Invalid;

        // Constant-time hash comparison.
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(user.PendingOtpHash),
            Encoding.UTF8.GetBytes(HashCode(code.Trim())));

        if (!matches)
        {
            user.PendingOtpAttempts++;
            await _userManager.UpdateAsync(user);
            var result = user.PendingOtpAttempts >= MaxAttempts
                ? OtpVerifyResult.TooManyAttempts
                : OtpVerifyResult.Invalid;
            await _audit.LogAsync(FcmsAuditActions.OtpFailed, nameof(FcmsUser), user.Id.ToString(),
                value: new { reason = result.ToString(), attempts = user.PendingOtpAttempts },
                severity: FcmsLogSeverity.Warning, ct: ct);
            return result;
        }

        // Clear the pending OTP — single use.
        user.PendingOtpHash = null;
        user.PendingOtpExpiresAt = null;
        user.PendingOtpAttempts = 0;
        await _userManager.UpdateAsync(user);
        await _audit.LogAsync(FcmsAuditActions.OtpVerified, nameof(FcmsUser), user.Id.ToString(), ct: ct);
        return OtpVerifyResult.Ok;
    }

    public async Task<bool> VerifyRecoveryCodeAsync(FcmsUser user, string code, CancellationToken ct = default)
    {
        if (user is null || string.IsNullOrWhiteSpace(code)) return false;
        var normalized = NormalizeRecoveryCode(code);
        var hash = HashCode(normalized);

        var rows = await _recovery.FindAsync(r => r.UserId == user.Id && !r.IsUsed, ct);
        var match = rows.FirstOrDefault(r => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(r.CodeHash),
            Encoding.UTF8.GetBytes(hash)));
        if (match is null) return false;

        match.IsUsed = true;
        match.UsedAt = FcmsTime.Now;
        await _recovery.UpdateAsync(match, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(FcmsAuditActions.RecoveryCodeUsed, nameof(FcmsUser), user.Id.ToString(), ct: ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> RegenerateRecoveryCodesAsync(FcmsUser user, int count = 10, CancellationToken ct = default)
    {
        if (user is null) return [];
        // Drop any existing codes — regeneration is a "new device" event.
        var existing = await _recovery.FindAsync(r => r.UserId == user.Id, ct);
        foreach (var row in existing)
            await _recovery.DeleteAsync(row, ct);

        var plaintext = new List<string>(capacity: count);
        for (var i = 0; i < count; i++)
        {
            var raw = GenerateRecoveryCode();
            plaintext.Add(raw);
            await _recovery.AddAsync(new FcmsRecoveryCode
            {
                UserId = user.Id,
                CodeHash = HashCode(NormalizeRecoveryCode(raw)),
            }, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return plaintext;
    }

    public async Task<int> CountUnusedRecoveryCodesAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await _recovery.FindAsync(r => r.UserId == userId && !r.IsUsed, ct);
        return rows.Count;
    }

    private async Task ClearPendingOtpAsync(FcmsUser user)
    {
        user.PendingOtpHash = null;
        user.PendingOtpExpiresAt = null;
        user.PendingOtpAttempts = 0;
        await _userManager.UpdateAsync(user);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string GenerateCode()
    {
        // RandomNumberGenerator → uniform 6-digit code. Padded with leading
        // zeros so codes like "000123" don't show as 3 digits.
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var n = BitConverter.ToUInt32(bytes) % 1_000_000;
        return n.ToString("D6");
    }

    /// <summary>10-char URL-safe alphabet (no 0/O/1/l) so written-down codes don't get misread.</summary>
    private static string GenerateRecoveryCode()
    {
        const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(11);
        for (var i = 0; i < 10; i++)
        {
            if (i == 5) sb.Append('-');   // ABCDE-FGHJK formatting
            sb.Append(Alphabet[bytes[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    private static string NormalizeRecoveryCode(string raw)
        => raw.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

    private static string HashCode(string plain)
    {
        // SHA-256 hex — fast, deterministic, and good enough for short-lived
        // OTPs (slow KDFs would only delay each verify with no real benefit
        // since codes expire in 5 min anyway). For the recovery codes the
        // alphabet entropy (~50 bits) sits well above brute-force range.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexString(bytes);
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var at = email.IndexOf('@');
        if (at <= 1) return email;   // "a@b.com" → unchanged (already minimal info)
        return email[0] + new string('*', Math.Max(1, at - 2)) + email[at - 1] + email[at..];
    }

    private static string MaskPhone(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4) return phone;
        // BD numbers: "01712345678" → "01*******678"
        var visible = Math.Min(3, phone.Length / 3);
        return phone[..2] + new string('*', phone.Length - 2 - visible) + phone[^visible..];
    }
}
