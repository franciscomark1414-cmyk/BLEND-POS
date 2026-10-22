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
            {
                var firstError = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault();

                ViewBag.Error = firstError ?? "Please check your input.";
                return View(model);
            }

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

            // ✅ IDAGDAG ITO: Record login time
            user.IsLoggedIn = true;
            user.LastLoginTime = DateTime.Now;
            await _context.SaveChangesAsync();

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
                var ownerId = HttpContext.Session.GetInt32("Owner_UserId");
                var staffId = HttpContext.Session.GetInt32("Staff_UserId");

                int? userIdToLogout = null;
                string sessionPrefixToClear = null;

                if (ownerId.HasValue && ownerId.Value > 0)
                {
                    userIdToLogout = ownerId.Value;
                    sessionPrefixToClear = "Owner_";
                }
                else if (staffId.HasValue && staffId.Value > 0)
                {
                    userIdToLogout = staffId.Value;
                    sessionPrefixToClear = "Staff_";
                }

                if (userIdToLogout.HasValue)
                {
                    var user = await _context.Users.FindAsync(userIdToLogout.Value);
                    if (user != null)
                    {
                        user.IsLoggedIn = false;
                        user.LastLogoutTime = DateTime.Now;
                        await _context.SaveChangesAsync();
                    }
                }

                // ✅ I-REMOVE LANG ANG SPECIFIC ROLE'S KEYS
                if (sessionPrefixToClear != null)
                {
                    HttpContext.Session.Remove(sessionPrefixToClear + "UserId");
                    HttpContext.Session.Remove(sessionPrefixToClear + "Username");
                    HttpContext.Session.Remove(sessionPrefixToClear + "UserRole");
                }

                // ✅ HUWAG BURAHIN ANG COOKIE KUNG MAY IBANG ROLE PA
                var stillHasOwner = HttpContext.Session.GetInt32("Owner_UserId").HasValue
                                 && HttpContext.Session.GetInt32("Owner_UserId").Value > 0;
                var stillHasStaff = HttpContext.Session.GetInt32("Staff_UserId").HasValue
                                 && HttpContext.Session.GetInt32("Staff_UserId").Value > 0;

                if (!stillHasOwner && !stillHasStaff)
                {
                    // Burahin lang ang cookie kung wala nang naka-login
                    HttpContext.Response.Cookies.Delete(".AspNetCore.Session");
                }

                return Json(new { success = true, message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}