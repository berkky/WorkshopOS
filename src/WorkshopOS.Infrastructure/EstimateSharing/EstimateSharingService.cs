using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WorkshopOS.Application.EstimateSharing;
using WorkshopOS.Domain.Estimates;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.EstimateSharing;

public sealed class EstimateSharingService : IEstimateSharingService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;
    private readonly IEstimateShareTokenGenerator _tokenGenerator;
    private readonly TimeProvider _timeProvider;

    public EstimateSharingService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext,
        IEstimateShareTokenGenerator tokenGenerator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
        _tokenGenerator = tokenGenerator;
        _timeProvider = timeProvider;
    }

    public async Task<EstimateShareManagementDetails?> GetEstimateShareDetailsAsync(
        Guid estimateId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return null;
        }

        var estimate = await _dbContext.Estimates.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var currentShare = await _dbContext.EstimateShares.AsNoTracking()
            .Where(candidate => candidate.EstimateId == estimateId && candidate.RevokedAtUtc == null)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .ThenByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var canShare = EstimateShareEligibilityPolicy.CanCreateShare(estimate.Status);
        var status = ResolveShareStatus(currentShare, now);

        return new EstimateShareManagementDetails
        {
            EstimateId = estimateId,
            CurrentShareId = currentShare?.Id,
            PublicId = currentShare?.PublicId,
            Status = status,
            CreatedAtUtc = currentShare?.CreatedAtUtc,
            ExpiresAtUtc = currentShare?.ExpiresAtUtc,
            RevokedAtUtc = currentShare?.RevokedAtUtc,
            LastAccessedAtUtc = currentShare?.LastAccessedAtUtc,
            PortalDecision = currentShare?.Decision,
            PortalDecisionAtUtc = currentShare?.DecisionAtUtc,
            CanCreate = canShare && currentShare is null,
            CanRotate = canShare && currentShare is not null,
            CanRevoke = currentShare is not null && !currentShare.IsRevoked,
        };
    }

    public Task<EstimateShareOperationResult<EstimateShareCreationResult>> CreateShareAsync(
        Guid actorUserId,
        CreateEstimateShareCommand command,
        CancellationToken cancellationToken = default) =>
        CreateOrRotateShareAsync(
            actorUserId,
            command.EstimateId,
            command.DurationDays,
            revokeExisting: false,
            cancellationToken);

    public Task<EstimateShareOperationResult<EstimateShareCreationResult>> RotateShareAsync(
        Guid actorUserId,
        RotateEstimateShareCommand command,
        CancellationToken cancellationToken = default) =>
        CreateOrRotateShareAsync(
            actorUserId,
            command.EstimateId,
            command.DurationDays,
            revokeExisting: true,
            cancellationToken);

    public async Task<EstimateShareOperationResult> RevokeShareAsync(
        Guid actorUserId,
        RevokeEstimateShareCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetOrganizationId(out _))
        {
            return EstimateShareOperationResult.Failed(EstimateShareOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateShareOperationResult.Failed(EstimateShareOperationFailureReason.Unauthorized);
        }

        var share = await _dbContext.EstimateShares
            .Where(candidate => candidate.EstimateId == command.EstimateId && candidate.RevokedAtUtc == null)
            .OrderByDescending(candidate => candidate.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (share is null)
        {
            return EstimateShareOperationResult.Failed(EstimateShareOperationFailureReason.ShareNotFound);
        }

        share.Revoke(_timeProvider.GetUtcNow(), actorUserId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return EstimateShareOperationResult.Succeeded();
    }

    private async Task<EstimateShareOperationResult<EstimateShareCreationResult>> CreateOrRotateShareAsync(
        Guid actorUserId,
        Guid estimateId,
        int durationDays,
        bool revokeExisting,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.OrganizationUnresolved);
        }

        if (!await ValidateManagerAsync(actorUserId, cancellationToken))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.Unauthorized);
        }

        if (!EstimateShareExpiryPolicy.IsValidDurationDays(durationDays))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.InvalidInput);
        }

        var estimate = await _dbContext.Estimates
            .SingleOrDefaultAsync(candidate => candidate.Id == estimateId, cancellationToken);

        if (estimate is null)
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.EstimateNotFound);
        }

        if (!EstimateShareEligibilityPolicy.CanCreateShare(estimate.Status))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.EstimateNotEligible);
        }

        var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
        IDbContextTransaction? transaction = null;
        if (ownsTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }

        try
        {
            if (revokeExisting)
            {
                var currentShare = await _dbContext.EstimateShares
                    .Where(candidate => candidate.EstimateId == estimateId && candidate.RevokedAtUtc == null)
                    .OrderByDescending(candidate => candidate.CreatedAtUtc)
                    .FirstOrDefaultAsync(cancellationToken);

                if (currentShare is null)
                {
                    return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                        EstimateShareOperationFailureReason.ShareNotFound);
                }

                currentShare.Revoke(_timeProvider.GetUtcNow(), actorUserId);
            }
            else
            {
                var hasCurrentShare = await _dbContext.EstimateShares
                    .AnyAsync(
                        candidate => candidate.EstimateId == estimateId && candidate.RevokedAtUtc == null,
                        cancellationToken);

                if (hasCurrentShare)
                {
                    return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                        EstimateShareOperationFailureReason.ActiveShareAlreadyExists);
                }
            }

            var rawToken = _tokenGenerator.GenerateRawToken();
            var tokenHash = EstimateShareTokenHasher.HashToHex(rawToken);
            var publicId = Guid.CreateVersion7();
            var expiresAtUtc = _timeProvider.GetUtcNow().AddDays(durationDays);

            var share = new EstimateShare(
                organizationId,
                publicId,
                estimateId,
                tokenHash,
                expiresAtUtc,
                actorUserId);

            _dbContext.EstimateShares.Add(share);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction && transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return EstimateShareOperationResult<EstimateShareCreationResult>.Succeeded(
                new EstimateShareCreationResult
                {
                    PublicId = publicId,
                    RawToken = rawToken,
                    ExpiresAtUtc = expiresAtUtc,
                });
        }
        catch (DbUpdateException exception) when (IsUniqueConflict(exception))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.ActiveShareAlreadyExists);
        }
        catch (DbUpdateException exception) when (IsSerializationConflict(exception))
        {
            return EstimateShareOperationResult<EstimateShareCreationResult>.Failed(
                EstimateShareOperationFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction && transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static EstimateShareStatus ResolveShareStatus(EstimateShare? share, DateTimeOffset utcNow)
    {
        if (share is null)
        {
            return EstimateShareStatus.None;
        }

        if (share.IsRevoked)
        {
            return EstimateShareStatus.Revoked;
        }

        if (share.IsExpired(utcNow))
        {
            return EstimateShareStatus.Expired;
        }

        return EstimateShareStatus.Active;
    }

    private async Task<bool> ValidateManagerAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
        {
            return false;
        }

        var role = await _dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == actorUserId
                                 && membership.OrganizationId == organizationId
                                 && membership.Status == OrganizationMembershipStatus.Active)
            .Select(membership => membership.Role)
            .SingleOrDefaultAsync(cancellationToken);

        return EstimateShareManagerPolicy.CanManageEstimateShares(role);
    }

    private static bool IsUniqueConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState is "23505")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSerializationConflict(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException && postgresException.SqlState is "40001")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (_organizationContext.IsResolved && _organizationContext.OrganizationId.HasValue)
        {
            organizationId = _organizationContext.OrganizationId.Value;
            return true;
        }

        organizationId = Guid.Empty;
        return false;
    }
}
