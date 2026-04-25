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

            // I-save sa session
            HttpContext.Session.SetInt32("UserId", user.Id);
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("UserRole", user.Role);

            // ✅ FIXED: Check for "owner" OR "employee"
            var roleLower = user.Role.Trim().ToLower();

            if (roleLower == "owner")
            {
                return RedirectToAction("Dashboard", "Owner");
            }
            else if (roleLower == "employee" || roleLower == "staff")
            {
                // Employee or Staff mapupunta sa POS
                return RedirectToAction("POS", "Staff");
            }
            else
            {
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