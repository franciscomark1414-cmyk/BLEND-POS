using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Infoman.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Infoman.Filters
{
    public class OwnerAuthorizationFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var session = context.HttpContext.Session;

            // Get owner session data
            var userId = session.GetInt32("Owner_UserId");
            var userRole = session.GetString("Owner_UserRole");

            // Check if session exists
            if (!userId.HasValue || userId.Value == 0 || userRole != "Owner")
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Verify with database
            var db = context.HttpContext.RequestServices.GetRequiredService<ConnectionDatabase>();
            var user = await db.Users.FindAsync(userId.Value);

            if (user == null || user.Role != "Owner")
            {
                session.Clear();
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // If all valid, proceed to the action
            await next();
        }
    }
}