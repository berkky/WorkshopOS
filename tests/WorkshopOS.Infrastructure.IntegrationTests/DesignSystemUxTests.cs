using WorkshopOS.Infrastructure.Authorization;

namespace WorkshopOS.Infrastructure.IntegrationTests;

public sealed class DesignSystemUxTests
{
    private static readonly string WebRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "WorkshopOS.Web"));

    private static string ReadView(string relativePath) =>
        File.ReadAllText(Path.Combine(WebRoot, relativePath));

    [Fact]
    public void PublicLayout_DoesNotRenderInternalNavigation()
    {
        var layout = ReadView("Views/Shared/_Layout.cshtml");
        Assert.DoesNotContain("asp-controller=\"Dashboard\"", layout);
        Assert.DoesNotContain("asp-controller=\"Operations\"", layout);
        Assert.DoesNotContain("Operations board", layout);
    }

    [Fact]
    public void CustomerPortalLayout_DoesNotRenderInternalNavigation()
    {
        var layout = ReadView("Views/Shared/_CustomerPortalLayout.cshtml");
        Assert.DoesNotContain("asp-controller=\"Dashboard\"", layout);
        Assert.DoesNotContain("asp-controller=\"RepairOrders\"", layout);
        Assert.DoesNotContain("wos-sidebar", layout);
    }

    [Fact]
    public void CustomerPortalLayout_DoesNotReferenceInspectionMedia()
    {
        var layout = ReadView("Views/Shared/_CustomerPortalLayout.cshtml");
        var estimate = ReadView("Views/Portal/Estimate.cshtml");
        Assert.DoesNotContain("inspection-media", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inspection-media", estimate, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecureShareCreatedPage_DoesNotRenderTokenHash()
    {
        var view = ReadView("Views/Estimates/ShareCreated.cshtml");
        Assert.DoesNotContain("TokenHash", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MajorPostForms_StillContainAntiforgery()
    {
        var repairOrderDetails = ReadView("Views/RepairOrders/Details.cshtml");
        var invoiceDetails = ReadView("Views/Invoices/Details.cshtml");
        var estimateDetails = ReadView("Views/Estimates/Details.cshtml");

        Assert.Contains("AntiForgeryToken", repairOrderDetails);
        Assert.Contains("AntiForgeryToken", invoiceDetails);
        Assert.Contains("AntiForgeryToken", estimateDetails);
    }

    [Fact]
    public void ReportingRoutes_RemainGetOnly()
    {
        var dashboard = ReadView("Controllers/DashboardController.cs");
        var reports = ReadView("Controllers/ReportsController.cs");

        Assert.Contains("[HttpGet(\"/dashboard\")]", dashboard);
        Assert.DoesNotContain("HttpPost", dashboard);
        Assert.Contains("[HttpGet(\"operations\")]", reports);
        Assert.Contains("[HttpGet(\"commercial\")]", reports);
        Assert.DoesNotContain("HttpPost", reports);
    }

    [Fact]
    public void CustomCssAssets_Exist()
    {
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/css/workshopos-tokens.css")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/css/workshopos-components.css")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/css/workshopos-shell.css")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/css/workshopos-public.css")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/js/site.js")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/js/workshopos-hero.js")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/images/workshopos/hero-vehicle-wireframe.svg")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "wwwroot/images/workshopos/onboarding-success.svg")));
    }
}
