using WorkshopOS.Domain.Common;

namespace WorkshopOS.Domain.Estimates;

public class EstimateShare : OrganizationOwnedEntity, IHasTimestamps
{
    public Guid PublicId { get; private set; }

    public Guid EstimateId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public EstimateShareDecision? Decision { get; private set; }

    public DateTimeOffset? DecisionAtUtc { get; private set; }

    public DateTimeOffset? LastAccessedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    protected EstimateShare()
    {
    }

    public EstimateShare(
        Guid organizationId,
        Guid publicId,
        Guid estimateId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId)
        : base(organizationId)
    {
        if (publicId == Guid.Empty)
        {
            throw new ArgumentException("Public identifier is required.", nameof(publicId));
        }

        if (estimateId == Guid.Empty)
        {
            throw new ArgumentException("Estimate identifier is required.", nameof(estimateId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("Creator user identifier is required.", nameof(createdByUserId));
        }

        PublicId = publicId;
        EstimateId = estimateId;
        TokenHash = tokenHash.Trim();
        ExpiresAtUtc = expiresAtUtc;
        CreatedByUserId = createdByUserId;
    }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= ExpiresAtUtc;

    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsExpired(utcNow);

    public void Revoke(DateTimeOffset revokedAtUtc, Guid revokedByUserId)
    {
        if (IsRevoked)
        {
            return;
        }

        if (revokedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Revoker user identifier is required.", nameof(revokedByUserId));
        }

        RevokedAtUtc = revokedAtUtc;
        RevokedByUserId = revokedByUserId;
    }

    public void RecordAccess(DateTimeOffset accessedAtUtc)
    {
        LastAccessedAtUtc = accessedAtUtc;
    }

    public void RecordDecision(EstimateShareDecision decision, DateTimeOffset decidedAtUtc)
    {
        if (Decision.HasValue)
        {
            throw new InvalidOperationException("Share decision has already been recorded.");
        }

        Decision = decision;
        DecisionAtUtc = decidedAtUtc;
    }
}
