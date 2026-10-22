using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Infoman.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Infoman.Filters
{
    public class StaffAuthorizationFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var session = context.HttpContext.Session;

            var userId = session.GetInt32("Staff_UserId");

            if (!userId.HasValue || userId.Value == 0)
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<ConnectionDatabase>();
            var user = await db.Users.FindAsync(userId.Value);

            if (user == null || (user.Role != "Employee" && user.Role != "Staff"))
            {
                // ✅ I-remove lang ang STAFF keys, wag ang buong session
                session.Remove("Staff_UserId");
                session.Remove("Staff_Username");
                session.Remove("Staff_UserRole");

                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }
            await next();
        }
    }
}