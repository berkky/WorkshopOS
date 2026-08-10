using System.Text.RegularExpressions;

namespace WorkshopOS.Infrastructure.IntegrationTests;

public sealed class SecurityReleaseReadinessTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", ".."));

    private static readonly string WebRoot = Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web");

    private static string ReadWebFile(string relativePath) =>
        File.ReadAllText(Path.Combine(WebRoot, relativePath));

    private static IEnumerable<string> EnumerateCsFiles(string relativeDirectory) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativeDirectory), "*.cs", SearchOption.AllDirectories);

    [Fact]
    public void AllowAnonymous_EndpointsMatchExpectedAllowlist()
    {
        var controllerSources = EnumerateCsFiles("src/WorkshopOS.Web/Controllers")
            .Select(File.ReadAllText)
            .ToList();

        var anonymousCount = controllerSources.Sum(source =>
            Regex.Matches(source, @"\[AllowAnonymous\]").Count);

        var anonymousControllers = EnumerateCsFiles("src/WorkshopOS.Web/Controllers")
            .Where(path => File.ReadAllText(path).Contains("[AllowAnonymous]", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(8, anonymousCount);
        Assert.Equal(3, anonymousControllers.Count);
        Assert.Contains("AccountController.cs", anonymousControllers);
        Assert.Contains("PortalController.cs", anonymousControllers);
        Assert.Contains("OnboardingController.cs", anonymousControllers);
    }

    [Fact]
    public void Startup_DoesNotAutoMigrateOrSeed()
    {
        var webSources = string.Join('\n', EnumerateCsFiles("src/WorkshopOS.Web"));
        Assert.DoesNotContain("Database.Migrate", webSources, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", webSources, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDeleted", webSources, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_UsesAntiforgeryOpenRedirectProtectionAndRateLimit()
    {
        var accountController = ReadWebFile("Controllers/AccountController.cs");
        Assert.Contains("[ValidateAntiForgeryToken]", accountController, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(\"StaffLogin\")]", accountController, StringComparison.Ordinal);
        Assert.Contains("Url.IsLocalUrl(returnUrl)", accountController, StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimitPolicies_IncludeOnboardingPortalAndStaffLogin()
    {
        var dependencyInjection = ReadWebFile("DependencyInjection.cs");
        Assert.Contains("OwnerOnboarding", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("CustomerPortalAccess", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("StaffLogin", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("PermitLimit = 10", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionPipeline_UsesExceptionHandlerAndHsts()
    {
        var dependencyInjection = ReadWebFile("DependencyInjection.cs");
        Assert.Contains("UseExceptionHandler(\"/Home/Error\")", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("UseHsts()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("UseHttpsRedirection()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("SecurityHeadersMiddleware", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorView_DoesNotAlwaysExposeDevelopmentGuidance()
    {
        var errorView = ReadWebFile("Views/Shared/Error.cshtml");
        Assert.Contains("HostEnvironment.IsDevelopment()", errorView, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroJavaScript_HasNoUnsafeInjectionPatterns()
    {
        var heroJs = ReadWebFile("wwwroot/js/workshopos-hero.js");
        Assert.DoesNotContain("innerHTML", heroJs, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", heroJs, StringComparison.Ordinal);
        Assert.DoesNotContain("Function(", heroJs, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", heroJs, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", heroJs, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroJavaScript_CancelsAnimationFrameOnPageHide()
    {
        var heroJs = ReadWebFile("wwwroot/js/workshopos-hero.js");
        Assert.Contains("cancelAnimationFrame", heroJs, StringComparison.Ordinal);
        Assert.Contains("pagehide", heroJs, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroCss_RespectsReducedMotion()
    {
        var publicCss = ReadWebFile("wwwroot/css/workshopos-public.css");
        Assert.Contains("prefers-reduced-motion: reduce", publicCss, StringComparison.Ordinal);
        Assert.Contains("wos-hero--reduced-motion", publicCss, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAssets_IncludeStep20Point1HeroFiles()
    {
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/js/workshopos-hero.js")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/images/workshopos/hero-vehicle-wireframe.svg")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/images/workshopos/onboarding-success.svg")));
    }

    [Fact]
    public void ApplicationCode_HasNoRawSqlExecution()
    {
        var applicationAndInfrastructure = string.Join(
            '\n',
            EnumerateCsFiles("src/WorkshopOS.Application")
                .Concat(EnumerateCsFiles("src/WorkshopOS.Infrastructure")));

        Assert.DoesNotContain("FromSqlRaw", applicationAndInfrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSqlRaw", applicationAndInfrastructure, StringComparison.Ordinal);
        Assert.DoesNotContain("NpgsqlCommand", applicationAndInfrastructure, StringComparison.Ordinal);
    }

    [Fact]
    public void StaffAndPortalCookies_UseDistinctSecureSettings()
    {
        var dependencyInjection = ReadWebFile("DependencyInjection.cs");
        Assert.Contains("WorkshopOS.Auth", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("WorkshopOS.CustomerPortal", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("CookieSecurePolicy.Always", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("HttpOnly = true", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void PortalBootstrap_ReadsHashAndPostsWithAntiforgery()
    {
        var accessView = ReadWebFile("Views/Portal/Access.cshtml");
        Assert.Contains("location.hash", accessView, StringComparison.Ordinal);
        Assert.Contains("replaceState", accessView, StringComparison.Ordinal);
        Assert.Contains("AntiForgeryToken", accessView, StringComparison.Ordinal);
        Assert.DoesNotContain("?token=", accessView, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InspectionMediaStorage_IsOutsideWwwroot()
    {
        var dependencyInjection = ReadWebFile("DependencyInjection.cs");
        Assert.Contains("App_Data", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("inspection-media", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("cannot be inside wwwroot", dependencyInjection, StringComparison.Ordinal);
    }
}
