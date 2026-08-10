using WorkshopOS.Application.Reporting;
using WorkshopOS.Application.Team;

namespace WorkshopOS.Web.Models.Reporting;

public sealed class ReportingFilterViewModel
{
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public Guid? WorkshopLocationId { get; set; }

    public IReadOnlyList<WorkshopLocationOption> LocationOptions { get; set; } =
        Array.Empty<WorkshopLocationOption>();
}

public sealed class DashboardViewModel
{
    public required ReportingFilterViewModel Filter { get; set; }

    public required ReportingContextHeader Header { get; set; }

    public required CurrentWorkshopSnapshot CurrentSnapshot { get; set; }

    public required SelectedPeriodPerformance SelectedPeriod { get; set; }
}

public sealed class OperationalReportViewModel
{
    public required ReportingFilterViewModel Filter { get; set; }

    public required OperationalReportResult Report { get; set; }
}

public sealed class CommercialReportViewModel
{
    public required ReportingFilterViewModel Filter { get; set; }

    public required CommercialReportResult Report { get; set; }
}
