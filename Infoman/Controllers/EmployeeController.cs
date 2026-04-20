using Infoman.Data;
using Infoman.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Infoman.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly ConnectionDatabase _db;

        public EmployeeController(ConnectionDatabase db)
        {
            _db = db;
        }

        // GET: Employee/GetAllEmployees
        [HttpGet]
        public async Task<IActionResult> GetAllEmployees()
        {
            try
            {
                // Case-insensitive filter
                var employees = await _db.Users
                    .Where(u => u.Role.ToLower() == "employee")
                    .Select(u => new
                    {
                        id = u.Id,
                        username = u.Username ?? "",
                        fullName = u.FullName ?? "",
                        phone = u.Phone ?? "",
                        email = u.Email ?? "",
                        status = u.Status ?? "Active",
                        dateJoined = u.DateJoined.ToString("yyyy-MM-dd")
                    })
                    .ToListAsync();

                return Ok(employees);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET: Employee/GetEmployee/{id}
        [HttpGet]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> GetEmployee(int id)
        {
            try
            {
                var employee = await _db.Users
                    .Where(u => u.Id == id && (u.Role == "Employee" || u.Role == "employee"))
                    .Select(u => new
                    {
                        success = true,
                        id = u.Id,
                        username = u.Username ?? "",
                        fullName = u.FullName ?? "",
                        phone = u.Phone ?? "",
                        email = u.Email ?? "",
                        status = u.Status ?? "Active",
                        dateJoined = u.DateJoined.ToString("yyyy-MM-dd")
                    })
                    .FirstOrDefaultAsync();

                if (employee == null)
                {
                    return Json(new { success = false, message = "Employee not found" });
                }

                return Json(employee);
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Employee/Create
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Create([FromBody] EmployeeCreateModel model)
        {
            try
            {
                if (model == null)
                {
                    return Json(new { success = false, message = "Invalid data" });
                }

                // Check if username already exists
                var existingUser = await _db.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == model.Username.ToLower());

                if (existingUser != null)
                {
                    return Json(new { success = false, message = "Username already exists" });
                }

                var employee = new User
                {
                    Username = model.Username.Trim(),
                    Password = model.Password,
                    Role = "Employee",
                    FullName = model.FullName?.Trim() ?? "",
                    Phone = model.Phone?.Trim(),
                    Email = model.Email?.Trim(),
                    Status = model.Status ?? "Active",
                    DateJoined = DateTime.Now
                };

                _db.Users.Add(employee);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Employee added successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Employee/Edit
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Edit([FromBody] EmployeeEditModel model)
        {
            try
            {
                var employee = await _db.Users.FindAsync(model.Id);
                if (employee == null)
                {
                    return Json(new { success = false, message = "Employee not found" });
                }

                employee.FullName = model.FullName?.Trim() ?? "";
                employee.Phone = model.Phone?.Trim();
                employee.Email = model.Email?.Trim();
                employee.Status = model.Status ?? "Active";

                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    employee.Password = model.Password;
                }

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Employee updated successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Employee/Delete
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var employee = await _db.Users.FindAsync(id);
                if (employee == null)
                {
                    return Json(new { success = false, message = "Employee not found" });
                }

                _db.Users.Remove(employee);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Employee deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Models
        public class EmployeeCreateModel
        {
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string FullName { get; set; } = "";
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string Status { get; set; } = "Active";
        }

        public class EmployeeEditModel
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string? Phone { get; set; }
            public string? Email { get; set; }
            public string Status { get; set; } = "Active";
            public string? Password { get; set; }
        }
    }
}