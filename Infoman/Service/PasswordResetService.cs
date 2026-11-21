using System.Security.Cryptography;
using System.Text;
using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Services
{
    public enum OtpVerifyResult { Success, Invalid }

    public class PasswordResetService
    {
        public const int OtpMinutes = 5;
        public const int MaxAttempts = 5;
        public const int CooldownSeconds = 60;
        public const int MaxRequestsPerHour = 5;

        private readonly ConnectionDatabase _db;
        private readonly IEmailSender _mail;
        private readonly ILogger<PasswordResetService> _log;
        private readonly byte[] _pepper;

        public PasswordResetService(ConnectionDatabase db, IEmailSender mail,
            IConfiguration cfg, ILogger<PasswordResetService> log)
        {
            _db = db; _mail = mail; _log = log;
            var pepper = cfg["PasswordReset:OtpPepper"];
            if (string.IsNullOrWhiteSpace(pepper))
                throw new InvalidOperationException("PasswordReset:OtpPepper is not configured.");
            _pepper = Encoding.UTF8.GetBytes(pepper);
        }

        private Task<User?> FindOwnerByEmail(string email)
        {
            var e = email.Trim().ToLower();
            return _db.Users.FirstOrDefaultAsync(u =>
                u.Email != null && u.Email.ToLower() == e &&
                u.Role.ToLower() == "owner" && !u.IsDeleted);
        }

        private string Hash(int userId, string code)
        {
            using var h = new HMACSHA256(_pepper);
            return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{code}")));
        }

        /// Always silent about whether the account exists.
        public async Task RequestOtpAsync(string email)
        {
            var user = await FindOwnerByEmail(email);
            if (user == null) return;

            var now = DateTime.UtcNow;

            var last = await _db.PasswordResetOtps.Where(o => o.UserId == user.Id)
                .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
            if (last != null && (now - last.CreatedAt).TotalSeconds < CooldownSeconds) return;

            var hourAgo = now.AddHours(-1);
            var recent = await _db.PasswordResetOtps.CountAsync(o => o.UserId == user.Id && o.CreatedAt > hourAgo);
            if (recent >= MaxRequestsPerHour) return;

            // Invalidate previous active OTPs
            var active = await _db.PasswordResetOtps.Where(o => o.UserId == user.Id && !o.IsUsed).ToListAsync();
            foreach (var a in active) a.IsUsed = true;

            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _db.PasswordResetOtps.Add(new PasswordResetOtp
            {
                UserId = user.Id,
                OtpHash = Hash(user.Id, code),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(OtpMinutes)
            });
            await _db.SaveChangesAsync();

            try
            {
                await _mail.SendAsync(user.Email!, "Your password reset code", BuildEmail(code, user.FullName));
            }
            catch (Exception ex)
            {
                // Never log the OTP or credentials
                _log.LogError(ex, "Failed to send password reset email for user {UserId}", user.Id);
            }
        }

        public async Task<(OtpVerifyResult Result, int? UserId)> VerifyOtpAsync(string email, string code)
        {
            var user = await FindOwnerByEmail(email);
            if (user == null) return (OtpVerifyResult.Invalid, null);

            var otp = await _db.PasswordResetOtps
                .Where(o => o.UserId == user.Id && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();

            if (otp == null || otp.ExpiresAt < DateTime.UtcNow) return (OtpVerifyResult.Invalid, null);

            var expected = Convert.FromBase64String(otp.OtpHash);
            var actual = Convert.FromBase64String(Hash(user.Id, code));

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                otp.FailedAttempts++;
                if (otp.FailedAttempts >= MaxAttempts) otp.IsUsed = true;
                await _db.SaveChangesAsync();
                return (OtpVerifyResult.Invalid, null);
            }

            otp.IsUsed = true; // single use
            await _db.SaveChangesAsync();
            return (OtpVerifyResult.Success, user.Id);
        }

        public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u =>
                u.Id == userId && u.Role.ToLower() == "owner" && !u.IsDeleted);
            if (user == null) return false;

            user.Password = new PasswordHasher<User>().HashPassword(user, newPassword.Trim());
            user.UpdatedAt = DateTime.Now;

            var leftovers = await _db.PasswordResetOtps.Where(o => o.UserId == userId && !o.IsUsed).ToListAsync();
            foreach (var o in leftovers) o.IsUsed = true;

            await _db.SaveChangesAsync();
            return true;
        }

        private static string BuildEmail(string code, string? name) => $@"
<div style='font-family:Segoe UI,Arial,sans-serif;max-width:480px;margin:auto;border:1px solid #eee;border-radius:12px;overflow:hidden'>
  <div style='background:#FF8C1A;color:#fff;padding:18px 24px;font-size:18px;font-weight:700'>Andeng's Mango &amp; More</div>
  <div style='padding:24px;color:#3B2F24'>
    <p>Hello {System.Net.WebUtility.HtmlEncode(name ?? "Owner")},</p>
    <p>Use this code to reset your Owner account password:</p>
    <p style='font-size:34px;letter-spacing:8px;font-weight:700;text-align:center;background:#FFF6E5;padding:14px;border-radius:10px'>{code}</p>
    <p>This code expires in <b>{OtpMinutes} minutes</b> and can be used once.</p>
    <p style='color:#7A6A5A;font-size:13px'>If you did not request this, ignore this email. Your password will not change.</p>
  </div>
</div>";
    }
}