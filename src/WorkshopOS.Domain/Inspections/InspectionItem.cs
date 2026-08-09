using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Inspections;

public class InspectionItem : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid InspectionId { get; private set; }

    public string Section { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public InspectionCondition Condition { get; private set; }

    public string? Notes { get; private set; }

    public int SortOrder { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected InspectionItem()
    {
    }

    public InspectionItem(
        Guid organizationId,
        Guid inspectionId,
        string section,
        string name,
        InspectionCondition condition = InspectionCondition.NotChecked,
        int sortOrder = 0,
        string? notes = null)
        : base(organizationId)
    {
        if (inspectionId == Guid.Empty)
        {
            throw new ArgumentException("Inspection identifier is required.", nameof(inspectionId));
        }

        if (string.IsNullOrWhiteSpace(section))
        {
            throw new ArgumentException("Inspection item section is required.", nameof(section));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Inspection item name is required.", nameof(name));
        }

        InspectionId = inspectionId;
        Section = section.Trim();
        Name = name.Trim();
        Condition = condition;
        SortOrder = sortOrder;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
