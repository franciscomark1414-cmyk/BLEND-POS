using Infoman.Data;
using Infoman.Models;
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

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(LoginViewModel model)
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

            // ✅ I-save ang user info sa session (optional pero recommended)
            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("UserRole", user.Role);

            // ✅ I-redirect base sa role
            if (user.Role.Trim().ToLower() == "owner")
            {
                return RedirectToAction("Dashboard", "Owner");
            }
            else if (user.Role.Trim().ToLower() == "staff")
            {
                return RedirectToAction("POS", "Staff");  // ← Staff mapupunta sa POS
            }
            else
            {
                // Kung hindi owner o staff, default sa Login
                ViewBag.Error = "Invalid User Role";
                return View(model);
            }
        }

        // Optional: Logout function
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}