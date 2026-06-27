using System.Globalization;
using System.Security.Claims;
using ClassicBlog.Components;
using ClassicBlog.Data;
using ClassicBlog.Models;
using ClassicBlog.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ClassicBlog;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // EF Core + SQLite
        builder.Services.AddDbContext<BlogDbContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

        // Authentication: cookie auth, no self-registration.
        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = "Cookie";
                options.DefaultChallengeScheme = "Cookie";
                options.DefaultSignInScheme = "Cookie";
                options.DefaultSignOutScheme = "Cookie";
            })
            .AddCookie("Cookie", options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Admin", policy => policy.RequireRole(Roles.Admin));
        });

        // Blazor Server: bridge the HTTP cookie auth state into the interactive circuit.
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

        // Accounts: PBKDF2 password hashing + user CRUD.
        builder.Services.AddSingleton<PasswordHasher<ApplicationUser>>();
        builder.Services.AddScoped<AccountService>();

        // Email (MailKit) + comment notifications.
        builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<CommentService>();

        // Simple visit counter.
        builder.Services.AddScoped<VisitCounterService>();

        // Localization (zh-CN default, en-US available). UI strings live in Resources/.
        // No ResourcesPath: the resx manifest name (ClassicBlog.Resources.SharedResource)
        // already matches the SharedResource type's full name, so the localizer resolves it.
        builder.Services.AddLocalization();
        var supportedCultures = new[] { new CultureInfo("zh-CN"), new CultureInfo("en-US") };
        builder.Services.Configure<RequestLocalizationOptions>(opts =>
        {
            opts.DefaultRequestCulture = new RequestCulture("zh-CN");
            opts.SupportedCultures = supportedCultures;
            opts.SupportedUICultures = supportedCultures;
        });

        var app = builder.Build();

        // Create the database (if needed) and seed sample posts on first run.
        using (var scope = app.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<BlogDbContext>();
            await db.Database.EnsureCreatedAsync();
            await db.EnsureSeedDataAsync();

            // Bootstrap an initial admin (idempotent) so login is possible.
            // Default credentials: admin / admin — change after first login.
            var accounts = sp.GetRequiredService<AccountService>();
            if (await accounts.FindByNameAsync("admin") is null)
            {
                var admin = await accounts.CreateAsync("admin", "admin", Roles.Admin);
                await accounts.SetEmailAsync(admin.Id, "admin@example.com");

                // Attribute the sample Welcome post to the admin so that
                // comment notifications on it have an author to notify.
                var welcome = await db.Posts.FirstOrDefaultAsync(
                    p => p.Slug == Post.MakeSlug("Welcome to ClassicBlog"));
                if (welcome is not null)
                {
                    welcome.AuthorId = admin.Id;
                    await db.SaveChangesAsync();
                }
            }
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        // Culture (from cookie) must be resolved before routing renders pages.
        app.UseRequestLocalization();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Cookie issuance/clearing must happen on an HTTP response, so login and
        // logout are minimal-API endpoints posted to by plain HTML forms.
        app.MapPost("/account/login", async (HttpContext ctx, IAntiforgery antiforgery, AccountService accounts) =>
        {
            await antiforgery.ValidateRequestAsync(ctx);
            var form = await ctx.Request.ReadFormAsync();
            var username = (string?)form["username"];
            var password = (string?)form["password"];
            var returnUrl = (string?)form["returnurl"];

            var user = string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
                ? null
                : await accounts.ValidateAsync(username, password);

            if (user is null)
            {
                ctx.Response.Redirect("/login?error=1");
                return;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role),
            };
            await ctx.SignInAsync(
                "Cookie",
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie")),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });

            ctx.Response.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/manage");
        });

        app.MapPost("/account/logout", async (HttpContext ctx, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(ctx);
            await ctx.SignOutAsync("Cookie", new AuthenticationProperties());
            ctx.Response.Redirect("/");
        });

        // Switch UI culture: sets the request-culture cookie and redirects back.
        app.MapPost("/culture/set", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var culture = (string?)form["culture"];
            var redirect = (string?)form["redirect"];

            if (culture == "zh-CN" || culture == "en-US")
            {
                ctx.Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) });
            }

            ctx.Response.Redirect(IsLocalReturnUrl(redirect) ? redirect! : "/");
        });

        app.Run();

        static bool IsLocalReturnUrl(string? url) =>
            !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//");
    }
}
