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
                var employees = await _db.Users
                    .Where(u => u.Role.ToLower() == "employee"
                        && !u.IsDeleted  // ✅ I-filter ang mga deleted
                        && u.Status != "Inactive")  // ✅ O i-filter ang inactive
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

        [HttpGet]
        public async Task<IActionResult> GetEmployeeOrdersByDate(int employeeId, string date)
        {
            try
            {
                var targetDate = DateTime.Parse(date).Date;

                var orders = await _db.Orders
                    .Where(o => o.StaffUserId == employeeId
                                && o.Date.Date == targetDate
                                && (o.Status == "COMPLETED" || o.Status == "VOIDED" || o.Status == "REFUNDED" || o.Status == "PENDING_BLENDING"))
                    .Select(o => new
                    {
                        o.OrderId,
                        o.OrderNumber,
                        o.TotalAmount,
                        o.Status,
                        o.Date,
                        o.ReturnStatus,
                        o.DiscountType,
                        o.DiscountAmount
                    })
                    .OrderBy(o => o.Date)
                    .ToListAsync();

                return Json(new { success = true, orders = orders, date = date });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, orders = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetEmployeeDailyOrders(int employeeId, int days = 30)
        {
            var startDate = DateTime.Today.AddDays(-days);

            // Get employee name
            var employee = await _db.Users.FindAsync(employeeId);
            var employeeName = employee?.FullName ?? "Unknown";

            // Get orders grouped by date (using StaffUserId)
            var dailyOrders = await _db.Orders
                .Where(o => o.StaffUserId == employeeId
                            && o.Date >= startDate)
                .GroupBy(o => o.Date.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    OrderCount = g.Count(),
                    TotalSales = g.Sum(o => o.TotalAmount),
                    Orders = g.Select(o => new
                    {
                        o.OrderId,
                        o.OrderNumber,
                        o.TotalAmount,
                        o.Status,
                        o.Date,
                        o.ReturnStatus,
                        o.DiscountType,
                        o.DiscountAmount
                    }).ToList()
                })
                .OrderByDescending(d => d.Date)
                .ToListAsync();

            return Json(new
            {
                success = true,
                dailyOrders = dailyOrders,
                employeeName = employeeName
            });
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

                // ✅ SERVER-SIDE PASSWORD VALIDATION
                if (string.IsNullOrEmpty(model.Password) || model.Password.Length < 8)
                {
                    return Json(new { success = false, message = "Password must be at least 8 characters long." });
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[A-Z]"))
                {
                    return Json(new { success = false, message = "Password must contain at least 1 uppercase letter." });
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[a-z]"))
                {
                    return Json(new { success = false, message = "Password must contain at least 1 lowercase letter." });
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[0-9]"))
                {
                    return Json(new { success = false, message = "Password must contain at least 1 number." });
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[!@#$%^&*(),.?"":{}|<>]"))
                {
                    return Json(new { success = false, message = "Password must contain at least 1 special character." });
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

                // Sa Edit method, bago i-save:
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    if (model.Password.Length < 8)
                    {
                        return Json(new { success = false, message = "Password must be at least 8 characters long." });
                    }

                    if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[A-Z]") ||
                        !System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[a-z]") ||
                        !System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[0-9]") ||
                        !System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[!@#$%^&*(),.?"":{}|<>]"))
                    {
                        return Json(new { success = false, message = "Password must contain uppercase, lowercase, number, and special character." });
                    }
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

                // ✅ SOFT DELETE - set IsDeleted = true
              //  employee.IsDeleted = true;
                employee.Status = "Inactive";

                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Employee deactivated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========== PERFORMANCE METHODS ==========

        // GET: Employee/GetPerformance/{userId}
        // GET: Employee/GetPerformance/{userId}
        [HttpGet]
        public async Task<IActionResult> GetPerformance(int userId, DateTime? startDate, DateTime? endDate)
        {
            var end = endDate ?? DateTime.Today;
            var start = startDate ?? end.AddDays(-30);

            var performances = await _db.EmployeePerformances
                .Where(p => p.UserId == userId && p.Date >= start && p.Date <= end)
                .OrderByDescending(p => p.Date)
                .ToListAsync();

            var employee = await _db.Users.FindAsync(userId);
            var totalSales = performances.Sum(p => p.TotalSales);
            var totalOrders = performances.Sum(p => p.OrdersProcessed);
            // var avgRating = performances.Any() ? performances.Average(p => p.Rating) : 0; // ← TANGGALIN

            return Json(new
            {
                success = true,
                employeeName = employee?.FullName,
                performances = performances.Select(p => new
                {
                    p.Date,
                    p.TotalSales,
                    p.OrdersProcessed,
                    // p.Rating,  // ← TANGGALIN
                    p.Notes
                }),
                summary = new
                {
                    totalSales,
                    totalOrders,
                    // averageRating = Math.Round(avgRating, 1),  // ← TANGGALIN
                    periodStart = start,
                    periodEnd = end
                }
            });
        }

        // POST: Employee/AddPerformance
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPerformance([FromBody] PerformanceCreateModel model)
        {
            try
            {
                var performance = new EmployeePerformance
                {
                    UserId = model.UserId,
                    Date = model.Date,
                    TotalSales = model.TotalSales,
                    OrdersProcessed = model.OrdersProcessed,
                    Rating = model.Rating,
                    Notes = model.Notes
                };

                _db.EmployeePerformances.Add(performance);

                // Update user's average rating and total orders
                var user = await _db.Users.FindAsync(model.UserId);
                if (user != null)
                {
                    var allPerformances = await _db.EmployeePerformances
                        .Where(p => p.UserId == model.UserId)
                        .ToListAsync();

                    user.AverageRating = allPerformances.Any()
                        ? allPerformances.Average(p => p.Rating)
                        : 0;
                    user.TotalOrdersProcessed = allPerformances.Sum(p => p.OrdersProcessed);
                }

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = "Performance recorded successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: Employee/GetAllPerformances (for admin/owner)
        [HttpGet]
        public async Task<IActionResult> GetAllPerformances(DateTime? startDate, DateTime? endDate)
        {
            var end = endDate ?? DateTime.Today;
            var start = startDate ?? end.AddDays(-30);

            var performances = await _db.EmployeePerformances
                .Include(p => p.Employee)
                .Where(p => p.Date >= start && p.Date <= end)
                .OrderByDescending(p => p.Date)
                .Select(p => new
                {
                    p.Id,
                    employeeName = p.Employee != null ? p.Employee.FullName : "Unknown",
                    p.UserId,
                    p.Date,
                    p.TotalSales,
                    p.OrdersProcessed,
                    p.Rating,
                    p.Notes
                })
                .ToListAsync();

            return Json(performances);
        }

        // ========== ATTENDANCE METHODS ==========

        // GET: Employee/GetAttendance/{userId}
        [HttpGet]
        public async Task<IActionResult> GetAttendance(int userId, int? year, int? month)
        {
            var targetYear = year ?? DateTime.Today.Year;
            var targetMonth = month ?? DateTime.Today.Month;

            var attendances = await _db.EmployeeAttendances
                .Where(a => a.UserId == userId && a.Date.Year == targetYear && a.Date.Month == targetMonth)
                .OrderBy(a => a.Date)
                .ToListAsync();

            var employee = await _db.Users.FindAsync(userId);

            var summary = new
            {
                present = attendances.Count(a => a.Status == AttendanceStatus.Present),
                absent = attendances.Count(a => a.Status == AttendanceStatus.Absent),
                late = attendances.Count(a => a.Status == AttendanceStatus.Late),
                onLeave = attendances.Count(a => a.Status == AttendanceStatus.OnLeave),
                halfDay = attendances.Count(a => a.Status == AttendanceStatus.HalfDay),
                totalHours = attendances.Sum(a => a.HoursWorked)
            };

            return Json(new
            {
                success = true,
                employeeName = employee?.FullName,
                attendances = attendances.Select(a => new
                {
                    a.Date,
                    status = a.Status.ToString(),
                    a.CheckInTime,
                    a.CheckOutTime,
                    a.HoursWorked,
                    a.LeaveReason,
                    a.Notes
                }),
                summary,
                year = targetYear,
                month = targetMonth
            });
        }

        // POST: Employee/AddAttendance
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAttendance([FromBody] AttendanceCreateModel model)
        {
            try
            {
                var attendance = new EmployeeAttendance
                {
                    UserId = model.UserId,
                    Date = model.Date,
                    Status = model.Status,
                    CheckInTime = model.CheckInTime,
                    CheckOutTime = model.CheckOutTime,
                    HoursWorked = model.HoursWorked,
                    LeaveReason = model.LeaveReason,
                    Notes = model.Notes
                };

                _db.EmployeeAttendances.Add(attendance);
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = "Attendance recorded successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: Employee/BulkAttendance (for marking multiple days)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkAttendance([FromBody] BulkAttendanceModel model)
        {
            try
            {
                foreach (var date in model.Dates)
                {
                    var existing = await _db.EmployeeAttendances
                        .FirstOrDefaultAsync(a => a.UserId == model.UserId && a.Date == date);

                    if (existing != null)
                    {
                        existing.Status = model.Status;
                        existing.Notes = model.Notes;
                    }
                    else
                    {
                        _db.EmployeeAttendances.Add(new EmployeeAttendance
                        {
                            UserId = model.UserId,
                            Date = date,
                            Status = model.Status,
                            Notes = model.Notes
                        });
                    }
                }

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"Attendance marked for {model.Dates.Count} day(s)" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========== MODELS FOR JSON ==========
        public class PerformanceCreateModel
        {
            public int UserId { get; set; }
            public DateTime Date { get; set; }
            public decimal TotalSales { get; set; }
            public int OrdersProcessed { get; set; }
            public decimal Rating { get; set; }
            public string? Notes { get; set; }
        }

        public class AttendanceCreateModel
        {
            public int UserId { get; set; }
            public DateTime Date { get; set; }
            public AttendanceStatus Status { get; set; }
            public TimeSpan? CheckInTime { get; set; }
            public TimeSpan? CheckOutTime { get; set; }
            public decimal HoursWorked { get; set; }
            public string? LeaveReason { get; set; }
            public string? Notes { get; set; }
        }

        public class BulkAttendanceModel
        {
            public int UserId { get; set; }
            public List<DateTime> Dates { get; set; } = new List<DateTime>();
            public AttendanceStatus Status { get; set; }
            public string? Notes { get; set; }
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