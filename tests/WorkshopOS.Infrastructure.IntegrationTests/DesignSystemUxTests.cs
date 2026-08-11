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
        var customerCreate = ReadView("Views/Customers/Create.cshtml");
        var appointmentCreate = ReadView("Views/Appointments/Create.cshtml");
        var inventoryAdjust = ReadView("Views/Inventory/Adjust.cshtml");

        Assert.Contains("AntiForgeryToken", repairOrderDetails);
        Assert.Contains("AntiForgeryToken", invoiceDetails);
        Assert.Contains("AntiForgeryToken", estimateDetails);
        Assert.Contains("AntiForgeryToken", customerCreate);
        Assert.Contains("AntiForgeryToken", appointmentCreate);
        Assert.Contains("AntiForgeryToken", inventoryAdjust);
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

    [Fact]
    public void AuthenticatedTheme_UsesDarkFirstTokenContract()
    {
        var tokens = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-tokens.css"));
        var components = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-components.css"));
        var shell = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-shell.css"));

        Assert.Contains("body.wos-app-body", tokens);
        Assert.Contains("--wos-bg-deep", tokens);
        Assert.Contains("--wos-surface-glass", tokens);
        Assert.Contains("--wos-border-accent", tokens);
        Assert.Contains("--wos-cyan", tokens);
        Assert.Contains("wos-preset-chip", components);
        Assert.Contains("wos-filter-bar", components);
        Assert.DoesNotContain("#eef0f4", shell);
    }

    [Fact]
    public void AuthenticatedAppShell_UsesPremiumShellContract()
    {
        var shell = ReadView("Views/Shared/_AppShell.cshtml");
        var tokens = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-tokens.css"));
        var components = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-components.css"));

        Assert.Contains("wos-app", shell);
        Assert.Contains("wos-app-body", shell);
        Assert.Contains("wos-sidebar", shell);
        Assert.Contains("workshopos-tokens.css", shell);
        Assert.Contains("workshopos-components.css", shell);
        Assert.Contains("workshopos-shell.css", shell);
        Assert.DoesNotContain("cdn.jsdelivr", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis", shell, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--wos-primary", tokens);
        Assert.Contains("wos-detail-hero", components);
        Assert.Contains("wos-page-header", components);
        Assert.Contains("wos-table-wrap", components);
    }

    [Fact]
    public void AuthenticatedDetailViews_UseWorkspaceHeaderAndStatusBadges()
    {
        var repairOrderDetails = ReadView("Views/RepairOrders/Details.cshtml");
        var customerDetails = ReadView("Views/Customers/Details.cshtml");
        var inspectionDetails = ReadView("Views/Inspections/Details.cshtml");

        Assert.Contains("_DetailWorkspaceHeader", repairOrderDetails);
        Assert.Contains("_StatusBadge", repairOrderDetails);
        Assert.Contains("StatusPresentation.ForRepairOrder", repairOrderDetails);
        Assert.Contains("_DetailWorkspaceHeader", customerDetails);
        Assert.Contains("_RelatedModule", customerDetails);
        Assert.Contains("wos-inspection-summary", inspectionDetails);
        Assert.Contains("StatusPresentation.ForInspectionCondition", inspectionDetails);
    }

    [Fact]
    public void AuthenticatedListViews_UsePremiumListPrimitives()
    {
        var repairOrders = ReadView("Views/RepairOrders/Index.cshtml");
        var appointments = ReadView("Views/Appointments/Index.cshtml");
        var invoices = ReadView("Views/Invoices/Index.cshtml");

        Assert.Contains("wos-page-header", repairOrders);
        Assert.Contains("wos-filter-bar", repairOrders);
        Assert.Contains("wos-table-wrap", repairOrders);
        Assert.Contains("_Pagination", repairOrders);
        Assert.Contains("_EmptyState", repairOrders);
        Assert.Contains("_Pagination", appointments);
        Assert.Contains("wos-mobile-list", invoices);
    }

    [Fact]
    public void SharedOperationalPartials_Exist()
    {
        Assert.True(File.Exists(Path.Combine(WebRoot, "Views/Shared/_DetailWorkspaceHeader.cshtml")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "Views/Shared/_Pagination.cshtml")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "Views/Shared/_RelatedModule.cshtml")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "Views/Shared/_StatusBadge.cshtml")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "Views/Shared/_FormValidationSummary.cshtml")));
    }

    [Fact]
    public void AuthenticatedCreateEditForms_UsePremiumFormShell()
    {
        var customerCreate = ReadView("Views/Customers/Create.cshtml");
        var vehicleCreate = ReadView("Views/Vehicles/Create.cshtml");
        var appointmentCreate = ReadView("Views/Appointments/Create.cshtml");
        var repairOrderCreate = ReadView("Views/RepairOrders/Create.cshtml");
        var inventoryAdjust = ReadView("Views/Inventory/Adjust.cshtml");
        var components = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-components.css"));

        Assert.Contains("wos-form-shell", customerCreate);
        Assert.Contains("wos-form-shell", vehicleCreate);
        Assert.Contains("wos-form-shell", appointmentCreate);
        Assert.Contains("wos-form-shell", repairOrderCreate);
        Assert.Contains("wos-form-shell", inventoryAdjust);
        Assert.Contains("wos-form-section", components);
        Assert.Contains("wos-form-actions", components);
        Assert.DoesNotContain("display-6", customerCreate);
        Assert.DoesNotContain("display-6", appointmentCreate);
    }

    [Fact]
    public void AppointmentDetailAndCalendar_UsePremiumWorkspaceContract()
    {
        var appointmentDetails = ReadView("Views/Appointments/Details.cshtml");
        var appointmentCalendar = ReadView("Views/Appointments/Calendar.cshtml");

        Assert.Contains("_DetailWorkspaceHeader", appointmentDetails);
        Assert.Contains("StatusPresentation.ForAppointment", appointmentDetails);
        Assert.Contains("wos-calendar-grid", appointmentCalendar);
        Assert.Contains("wos-calendar-event", appointmentCalendar);
        Assert.DoesNotContain("display-6", appointmentDetails);
        Assert.DoesNotContain("display-6", appointmentCalendar);
    }
}
