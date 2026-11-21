using System.ComponentModel.DataAnnotations;
using Infoman.Services;
using Microsoft.AspNetCore.Mvc;

namespace Infoman.Controllers
{
    public class ForgotPasswordVM { [Required, EmailAddress, StringLength(100)] public string Email { get; set; } = ""; }
    public class VerifyOtpVM { [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the 6-digit code.")] public string Code { get; set; } = ""; }
    public class ResetPasswordVM
    {
        [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        public string NewPassword { get; set; } = "";
        [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    [AutoValidateAntiforgeryToken]
    public class PasswordResetController : Controller
    {
        private const string KEmail = "PwdReset_Email";
        private const string KUserId = "PwdReset_UserId";
        private const string KUntil = "PwdReset_Until";
        private readonly PasswordResetService _svc;

        public PasswordResetController(PasswordResetService svc) => _svc = svc;

        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordVM());

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordVM model)
        {
            if (!ModelState.IsValid) return View(model);

            await _svc.RequestOtpAsync(model.Email);

            HttpContext.Session.SetString(KEmail, model.Email.Trim());
            TempData["Info"] = "If that email belongs to the Owner account, a verification code has been sent.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        [HttpGet]
        public IActionResult VerifyOtp()
        {
            if (HttpContext.Session.GetString(KEmail) == null) return RedirectToAction(nameof(ForgotPassword));
            return View(new VerifyOtpVM());
        }

        [HttpPost]
        public async Task<IActionResult> VerifyOtp(VerifyOtpVM model)
        {
            var email = HttpContext.Session.GetString(KEmail);
            if (email == null) return RedirectToAction(nameof(ForgotPassword));
            if (!ModelState.IsValid) return View(model);

            var (result, userId) = await _svc.VerifyOtpAsync(email, model.Code);
            if (result != OtpVerifyResult.Success)
            {
                ModelState.AddModelError("", "Invalid or expired code.");
                return View(new VerifyOtpVM());
            }

            HttpContext.Session.SetInt32(KUserId, userId!.Value);
            HttpContext.Session.SetString(KUntil, DateTime.UtcNow.AddMinutes(10).Ticks.ToString());
            return RedirectToAction(nameof(ResetPassword));
        }

        [HttpPost]
        public async Task<IActionResult> ResendOtp()
        {
            var email = HttpContext.Session.GetString(KEmail);
            if (email == null) return RedirectToAction(nameof(ForgotPassword));

            await _svc.RequestOtpAsync(email); // cooldown/rate limit enforced server-side
            TempData["Info"] = "If a new code is allowed, it has been sent. Please wait 60 seconds between requests.";
            return RedirectToAction(nameof(VerifyOtp));
        }

        [HttpGet]
        public IActionResult ResetPassword()
        {
            if (!HasGrant(out _)) return RedirectToAction(nameof(ForgotPassword));
            return View(new ResetPasswordVM());
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(ResetPasswordVM model)
        {
            if (!HasGrant(out var userId)) return RedirectToAction(nameof(ForgotPassword));
            if (!ModelState.IsValid) return View(model);

            if (string.IsNullOrWhiteSpace(model.NewPassword) || model.NewPassword.Trim().Length < 8)
            {
                ModelState.AddModelError(nameof(model.NewPassword), "Password must be at least 8 characters.");
                return View(model);
            }

            var ok = await _svc.ResetPasswordAsync(userId, model.NewPassword);

            HttpContext.Session.Remove(KUserId);
            HttpContext.Session.Remove(KUntil);
            HttpContext.Session.Remove(KEmail);

            if (!ok) return RedirectToAction(nameof(ForgotPassword));

            TempData["ResetSuccess"] = "Password reset successful. Please log in with your new password.";
            return RedirectToAction("Login", "Account");
        }

        private bool HasGrant(out int userId)
        {
            userId = HttpContext.Session.GetInt32(KUserId) ?? 0;
            var until = HttpContext.Session.GetString(KUntil);
            return userId > 0 && long.TryParse(until, out var t) && t > DateTime.UtcNow.Ticks;
        }
    }
}