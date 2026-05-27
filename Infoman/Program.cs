using Infoman.Data;
using Microsoft.EntityFrameworkCore;
using Infoman.Service;
using Infoman.Filters;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ConnectionDatabase>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ Add IHttpContextAccessor
builder.Services.AddHttpContextAccessor();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(120); // 2 hours
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
});

builder.Services.AddScoped<OwnerAuthorizationFilter>();
builder.Services.AddScoped<StaffAuthorizationFilter>();

// ========== REGISTER CUSTOM SERVICES ==========
builder.Services.AddScoped<FIFOService>();
builder.Services.AddScoped<VoidRefundService>();
builder.Services.AddScoped<RefundService>();
builder.Services.AddScoped<ExchangeService>();
// ==============================================

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["Expires"] = "0";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();

// ✅ Session MUST be between UseRouting and UseAuthorization
app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=LogIn}/{id?}")
    .WithStaticAssets();

app.Run();