using Infoman.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Infoman.Filters
{
    public class StaffSessionValidationFilter : IAsyncActionFilter
    {
        private readonly ConnectionDatabase _db;

        public StaffSessionValidationFilter(ConnectionDatabase db)
        {
            _db = db;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            // 1. IGNORE login/logout (PERO HINDI ANG PINGSESSION!)
            var path = http.Request.Path.Value?.ToLower() ?? "";
            if (path.Contains("/account/login") || path.Contains("/account/logout"))
            {
                await next();
                return;
            }

            var staffId = http.Session.GetInt32("Staff_UserId");
            var ownerId = http.Session.GetInt32("Owner_UserId");

            if (staffId == null && ownerId == null)
            {
                await next();
                return;
            }

            var userId = ownerId ?? staffId;

            // 2. CHECK DB
            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            bool isInvalid = user == null
                             || user.IsDeleted
                             || (user.Status != null && user.Status.Equals("Inactive", StringComparison.OrdinalIgnoreCase));

            if (!isInvalid)
            {
                await next();
                return;
            }

            // 🔴 3. INVALID USER — I-clear ang session
            http.Session.Clear();

            // ✅ 4. I-detect kung AJAX/JSON (para magbalik ng 401 instead na Redirect)
            var request = http.Request;
            bool isAjax = request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                          request.Headers["Accept"].ToString().Contains("application/json") ||
                          request.Headers["Content-Type"].ToString().Contains("application/json");

            if (isAjax || path.Contains("/pingsession"))
            {
                context.Result = new JsonResult(new
                {
                    success = false,
                    accountDeleted = true,
                    message = "Your account has been deactivated by the owner. You will be logged out."
                })
                {
                    StatusCode = 401
                };
                return;
            }

            context.Result = new RedirectToActionResult("Login", "Account", new { reason = "account_deleted" });
        }
    }
}