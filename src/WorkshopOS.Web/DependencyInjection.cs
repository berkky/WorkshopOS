using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WorkshopOS.Infrastructure;
using WorkshopOS.Infrastructure.Authorization;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.InspectionMedia;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WorkshopOS.Web.Authentication;
using WorkshopOS.Web.Health;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Middleware;
using WorkshopOS.Web.Presentation;
using WorkshopOS.Web.Services;
using WorkshopOS.Web;

namespace WorkshopOS.Web;

internal static class DependencyInjection
{
    public static IServiceCollection AddWorkshopOsWebServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var workshopOsConnectionString = configuration.GetConnectionString("WorkshopOS");
        if (string.IsNullOrWhiteSpace(workshopOsConnectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'WorkshopOS' is not configured. Set ConnectionStrings:WorkshopOS via User Secrets for local development.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(workshopOsConnectionString));

        services.AddScoped<ScopedOrganizationContext>();
        services.AddScoped<IOrganizationContext>(provider => provider.GetRequiredService<ScopedOrganizationContext>());
        services.AddScoped<IOrganizationContextMutator>(provider => provider.GetRequiredService<ScopedOrganizationContext>());
        services.AddSingleton(TimeProvider.System);
        var mediaStorageRoot = configuration.GetValue<string>($"{InspectionMediaStorageOptions.SectionName}:StorageRootPath")
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "inspection-media");
        var resolvedMediaRoot = Path.GetFullPath(mediaStorageRoot);
        var resolvedWwwRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (resolvedMediaRoot.StartsWith(resolvedWwwRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(resolvedMediaRoot, resolvedWwwRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Inspection media storage root cannot be inside wwwroot.");
        }

        services.Configure<InspectionMediaStorageOptions>(options =>
        {
            options.StorageRootPath = resolvedMediaRoot;
        });
        services.AddSingleton<WorkshopOS.Application.InspectionMedia.IInspectionMediaStorage, FileSystemInspectionMediaStorage>();
        services.AddWorkshopOsInfrastructure();

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 9 * 1024 * 1024;
        });

        services
            .AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 12;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "WorkshopOS.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/account/login";
            options.AccessDeniedPath = "/account/access-denied";
        });

        services.AddAuthentication()
            .AddCookie(CustomerPortalAuthDefaults.AuthenticationScheme, options =>
            {
                options.Cookie.Name = "WorkshopOS.CustomerPortal";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromHours(2);
                options.SlidingExpiration = false;
                options.LoginPath = "/portal/access/unavailable";
                options.AccessDeniedPath = "/portal/access/unavailable";
                options.EventsType = typeof(CustomerPortalCookieEvents);
            });

        services.AddScoped<CustomerPortalCookieEvents>();

        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.OrganizationMember, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new OrganizationMemberRequirement()))
            .AddPolicy(PolicyNames.OrganizationManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new OrganizationManagerRequirement()))
            .AddPolicy(PolicyNames.CustomerManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CustomerManagerRequirement()))
            .AddPolicy(PolicyNames.VehicleManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new VehicleManagerRequirement()))
            .AddPolicy(PolicyNames.AppointmentManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new AppointmentManagerRequirement()))
            .AddPolicy(PolicyNames.RepairOrderManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new RepairOrderManagerRequirement()))
            .AddPolicy(PolicyNames.InspectionManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new InspectionManagerRequirement()))
            .AddPolicy(PolicyNames.EstimateManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new EstimateManagerRequirement()))
            .AddPolicy(PolicyNames.CatalogManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new CatalogManagerRequirement()))
            .AddPolicy(PolicyNames.InventoryManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new InventoryManagerRequirement()))
            .AddPolicy(PolicyNames.BillingManager, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new BillingManagerRequirement()))
            .AddPolicy(PolicyNames.ReportingViewer, policy =>
                policy.RequireAuthenticatedUser()
                    .AddRequirements(new ReportingViewerRequirement()));

        services.AddScoped<IAuthorizationHandler, OrganizationMemberAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, OrganizationManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CustomerManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, VehicleManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, AppointmentManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, RepairOrderManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, InspectionManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, EstimateManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CatalogManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, InventoryManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, BillingManagerAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, ReportingViewerAuthorizationHandler>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("OwnerOnboarding", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                    }));
            options.AddPolicy("CustomerPortalAccess", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                    }));
            options.AddPolicy("StaffLogin", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                    }));
        });

        services.AddLocalization(options => options.ResourcesPath = "Resources");

        services
            .AddControllersWithViews()
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (_, factory) =>
                    factory.Create(typeof(SharedResource));
            });

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = new[]
            {
                WorkshopCultures.EnglishCulture,
                WorkshopCultures.TurkishCulture,
            };

            options.DefaultRequestCulture = new RequestCulture(WorkshopCultures.English);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.ApplyCurrentCultureToResponseHeaders = true;
            options.RequestCultureProviders =
            [
                new CookieRequestCultureProvider(),
                new AcceptLanguageHeaderRequestCultureProvider(),
            ];
        });

        services.AddScoped<IStatusPresentation, LocalizedStatusPresentation>();
        services.AddScoped<IWorkshopUiFormatting, WorkshopUiFormatting>();
        services.AddScoped<IAccountCenterService, AccountCenterService>();
        services.AddScoped<IWebFailureMessages, WebFailureMessages>();

        services.AddHealthChecks()
            .AddCheck<PostgreSqlReadinessHealthCheck>("postgresql", tags: ["ready"]);

        return services;
    }

    public static WebApplication UseWorkshopOsWebPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Home/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseRouting();

        var localizationOptions = app.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>()
            .Value;
        app.UseRequestLocalization(localizationOptions);

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<OrganizationResolutionMiddleware>();
        app.UseAuthorization();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthCheckResponseWriter.WriteMinimalResponseAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteMinimalResponseAsync,
        });

        app.MapStaticAssets();
        app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
            .WithStaticAssets();

        return app;
    }
}
