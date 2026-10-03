using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Api;
using SierraBusiness.Domain;
using SierraBusiness.Infrastructure;
using SierraBusiness.Web.Components;
using SierraBusiness.Web;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(o => o.MaximumReceiveMessageSize = 512 * 1024); // document thumbnails come back from the browser
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.AddIdentityCore<IdentityUser>(o => { o.Password.RequiredLength = 12; o.Lockout.MaxFailedAccessAttempts = 5; })
    .AddEntityFrameworkStores<BusinessDb>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(o => { o.LoginPath = "/account/login"; o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false; });
builder.Services.Configure<TradingOptions>(builder.Configuration.GetSection("Trading"));
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("Business") ?? "Data Source=App_Data/sierra.db";
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContextFactory<BusinessDb>(o => { if (provider == "SqlServer") o.UseSqlServer(connection); else o.UseSqlite(connection); });
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IdentityActorValidator>();
builder.Services.AddScoped<BusinessService>();
builder.Services.AddScoped<IBusinessService, WebBusinessService>();
builder.Services.AddScoped<IActorSession, WebActorSession>();
builder.Services.AddScoped<IPosService, PosService>();
builder.Services.AddScoped<IBankingService, BankingService>();
builder.Services.AddScoped<ICommerceService, CommerceService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IInventoryManagement, InventoryManagement>();
builder.Services.AddScoped<IPayrollService, PayrollService>();
builder.Services.AddScoped<IPartyService, PartyService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
builder.Services.AddScoped<IUserAdministration, UserAdministration>();
builder.Services.AddScoped<IPeriodService, PeriodService>();
builder.Services.AddScoped<IIntegrityService, IntegrityService>();
builder.Services.AddScoped<IImportService, SierraBusiness.Infrastructure.Imports.ImportService>();
builder.Services.AddScoped<IPurchasingService, PurchasingService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<ITenderService, TenderService>();
builder.Services.AddSingleton<IFileStore, DatabaseFileStore>();
builder.Services.AddScoped<IDocumentLibrary, DocumentLibrary>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (AntiforgeryValidationException)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(title: "Invalid request verification token", statusCode: 400).ExecuteAsync(context);
    }
});
app.UseStatusCodePages();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapHealthChecks("/health");
app.MapBusinessEndpoints();

// Database migration is an explicit deployment command, never an implicit production startup action.
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BusinessDb>();
    await HistoryProtection.MigrateAsync(db); // Lifts database guards only for the upgrade, then reinstates and re-seals.
    return;
}
if (args.Contains("--create-owner"))
{
    using var scope = app.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var email = builder.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Set Bootstrap__Email.");
    var password = builder.Configuration["Bootstrap:Password"] ?? throw new InvalidOperationException("Set Bootstrap__Password.");
    if (await users.FindByEmailAsync(email) is not null) throw new InvalidOperationException("This owner already exists.");
    var user = new IdentityUser(email) { Email = email, EmailConfirmed = true };
    var result = await users.CreateAsync(user, password);
    if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
    var claims = BusinessPermissions.All.Select(x => new Claim("permission", x)).Append(new Claim("branch", builder.Configuration["Trading:Branch"] ?? "FREETOWN"));
    var claimResult = await users.AddClaimsAsync(user, claims);
    if (!claimResult.Succeeded) throw new InvalidOperationException("Owner permission assignment failed.");
    return;
}
app.MapGet("/account/login", (HttpContext context, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(context);
    var error = context.Request.Query.ContainsKey("failed") ? "Sign-in failed. Check your credentials or wait if your account is locked." : "";
    return Results.Content($"""
    <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Sign in · Sierra Business</title><link rel="stylesheet" href="/app.css"></head>
    <body class="login-body"><main class="login-panel"><div class="brand"><span class="brand-mark">S</span><div>SIERRA<span>BUSINESS OS</span></div></div><p class="eyebrow">YOUR BRANCH WORKSPACE</p><h1>Welcome back.</h1><p>Sign in to manage stock, sales and accounts.</p><p role="alert">{error}</p><form method="post" action="/account/login"><input type="hidden" name="{token.FormFieldName}" value="{token.RequestToken}"><label>Email<input type="email" name="email" autocomplete="username" required autofocus></label><label>Password<input type="password" name="password" autocomplete="current-password" required></label><button class="primary">Sign in →</button></form><p class="hint">Access is restricted to your assigned branch and permissions.</p></main></body></html>
    """, "text/html");
});
app.MapPost("/account/login", async (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) =>
{
    await antiforgery.ValidateRequestAsync(context);
    var form = await context.Request.ReadFormAsync();
    var result = await signIn.PasswordSignInAsync(form["email"].ToString(), form["password"].ToString(), false, true);
    if (result.Succeeded)
    {
        var user = await signIn.UserManager.FindByNameAsync(form["email"].ToString());
        await signIn.SignInWithClaimsAsync(user!, false, [new Claim("session.expires", DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))]);
    }
    return Results.LocalRedirect(result.Succeeded ? "/" : "/account/login?failed=1");
});
app.MapGet("/account/logout", (HttpContext context, IAntiforgery antiforgery) =>
{
    var token = antiforgery.GetAndStoreTokens(context);
    return Results.Content($"<form method='post' action='/account/logout'><input type='hidden' name='{token.FormFieldName}' value='{token.RequestToken}'><button>Confirm sign out</button></form>", "text/html");
}).RequireAuthorization();
app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<IdentityUser> signIn) => { await antiforgery.ValidateRequestAsync(context); await signIn.SignOutAsync(); return Results.LocalRedirect("/account/login"); }).RequireAuthorization();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();




