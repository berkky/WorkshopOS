using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkshopOS.Infrastructure.Identity;
using WorkshopOS.Infrastructure.Persistence;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class IdentityTestServiceFactory
{
    private const string TestPasswordPolicy = "ValidPass123!";

    public static string ValidTestPassword => TestPasswordPolicy;

    public static UserManager<ApplicationUser> CreateUserManager(AppDbContext context)
    {
        return BuildServiceProvider(context).GetRequiredService<UserManager<ApplicationUser>>();
    }

    public static SignInManager<ApplicationUser> CreateSignInManager(AppDbContext context)
    {
        return BuildServiceProvider(context).GetRequiredService<SignInManager<ApplicationUser>>();
    }

    private static ServiceProvider BuildServiceProvider(AppDbContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection();
        services.AddSingleton(context);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
            .AddCookie(IdentityConstants.ApplicationScheme);
        services
            .AddIdentityCore<ApplicationUser>(options =>
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
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }
}
