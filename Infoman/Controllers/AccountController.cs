using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace Infoman.Controllers
{
    public class AccountController : Controller
    {
        private readonly ConnectionDatabase _context;

        public AccountController(ConnectionDatabase context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // ✅ NO AUTO-REDIRECT - para makapag-login sa kabilang tab
          
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var username = model.Username.Trim();
            var password = model.Password.Trim();

            var user = _context.Users
                .FirstOrDefault(u => u.Username.Trim().ToLower() == username.ToLower()
                                  && u.Password.Trim() == password);

            if (user == null)
            {
                ViewBag.Error = "Invalid Username or Password";
                return View(model);
            }

            var roleLower = user.Role.Trim().ToLower();

            if (roleLower != "owner")
            {
                if (user.Status != null && user.Status != "Active")
                {
                    ViewBag.Error = "Your account is inactive. Please contact the owner.";
                    return View(model);
                }
            }

            // ✅ USE PREFIXED SESSION KEYS
            string sessionPrefix = roleLower == "owner" ? "Owner_" : "Staff_";
            HttpContext.Session.SetInt32(sessionPrefix + "UserId", user.Id);
            HttpContext.Session.SetString(sessionPrefix + "Username", user.Username);
            HttpContext.Session.SetString(sessionPrefix + "UserRole", user.Role);

            // ✅ DO NOT clear the other role's session

            await HttpContext.Session.CommitAsync();

            if (roleLower == "owner")
            {
                return RedirectToAction("Dashboard", "Owner");
            }
            else
            {
                return RedirectToAction("POS", "Staff");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // ✅ UPDATE LOGOUT STATUS (kung may IsLoggedIn field ka)
                var userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue)
                {
                    var user = await _context.Users.FindAsync(userId.Value);
                    if (user != null)
                    {
                        user.IsLoggedIn = false;
                        user.LastLogoutTime = DateTime.Now;
                        await _context.SaveChangesAsync();
                    }
                }

                // Clear all session data
                HttpContext.Session.Clear();

                // Remove session cookie
                HttpContext.Response.Cookies.Delete(".AspNetCore.Session");

                return Json(new { success = true, message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}