using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using System.Linq;

namespace Infoman.Controllers
{
    public class AccountController:Controller
    {
        private readonly ConnectionDatabase _context;

        public AccountController(ConnectionDatabase context) {

            _context = context;

        }
        public IActionResult Login() { 
         
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

            if (user.Role.Trim().ToLower() == "owner")
                return RedirectToAction("Dashboard", "Owner");
            else
                return RedirectToAction("Sales", "Staff");
        }
    }
}
