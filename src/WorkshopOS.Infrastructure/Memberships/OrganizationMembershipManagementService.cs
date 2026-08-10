using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WorkshopOS.Application.Memberships;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.Memberships;

public sealed class OrganizationMembershipManagementService : IOrganizationMembershipManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IOrganizationContext _organizationContext;

    public OrganizationMembershipManagementService(
        AppDbContext dbContext,
        IOrganizationContext organizationContext)
    {
        _dbContext = dbContext;
        _organizationContext = organizationContext;
    }

    public Task<MembershipManagementResult> ChangeRoleAsync(
        ChangeMembershipRoleCommand command,
        CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(
            command.ActorUserId,
            command.MembershipId,
            cancellationToken,
            async (actorMembership, targetMembership) =>
            {
                if (MembershipRolePolicy.IsSelfMutation(actorMembership.UserId, targetMembership.UserId))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.SelfMutationRejected);
                }

                if (!MembershipRolePolicy.CanActorManageTargetRole(actorMembership.Role, targetMembership.Role)
                    || !MembershipRolePolicy.CanActorAssignRole(actorMembership.Role, command.NewRole))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.ActorNotAuthorized);
                }

                if (targetMembership.Role == OrganizationMembershipRole.Owner
                    && command.NewRole != OrganizationMembershipRole.Owner
                    && targetMembership.Status == OrganizationMembershipStatus.Active
                    && await IsLastActiveOwnerAsync(targetMembership, cancellationToken))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.LastOwnerProtected);
                }

                targetMembership.ChangeRole(command.NewRole);
                return MembershipManagementResult.Succeeded();
            });

    public Task<MembershipManagementResult> ChangeStatusAsync(
        ChangeMembershipStatusCommand command,
        CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(
            command.ActorUserId,
            command.MembershipId,
            cancellationToken,
            async (actorMembership, targetMembership) =>
            {
                if (MembershipRolePolicy.IsSelfMutation(actorMembership.UserId, targetMembership.UserId))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.SelfMutationRejected);
                }

                if (!MembershipRolePolicy.CanActorManageTargetRole(actorMembership.Role, targetMembership.Role))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.ActorNotAuthorized);
                }

                if (command.NewStatus is not (
                    OrganizationMembershipStatus.Active
                    or OrganizationMembershipStatus.Suspended
                    or OrganizationMembershipStatus.Revoked))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.InvalidRoleTransition);
                }

                if (targetMembership.Role == OrganizationMembershipRole.Owner
                    && command.NewStatus != OrganizationMembershipStatus.Active
                    && targetMembership.Status == OrganizationMembershipStatus.Active
                    && await IsLastActiveOwnerAsync(targetMembership, cancellationToken))
                {
                    return MembershipManagementResult.Failed(MembershipManagementFailureReason.LastOwnerProtected);
                }

                targetMembership.ChangeStatus(command.NewStatus);
                return MembershipManagementResult.Succeeded();
            });

    private async Task<MembershipManagementResult> MutateMembershipAsync(
        Guid actorUserId,
        Guid membershipId,
        CancellationToken cancellationToken,
        Func<OrganizationMembership, OrganizationMembership, Task<MembershipManagementResult>> mutate)
    {
        if (!_organizationContext.IsResolved || !_organizationContext.OrganizationId.HasValue)
        {
            return MembershipManagementResult.Failed(MembershipManagementFailureReason.OrganizationUnresolved);
        }

        var organizationId = _organizationContext.OrganizationId.Value;
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
            var actorMembership = await _dbContext.OrganizationMemberships
                .SingleOrDefaultAsync(
                    membership => membership.OrganizationId == organizationId
                                  && membership.UserId == actorUserId
                                  && membership.Status == OrganizationMembershipStatus.Active,
                    cancellationToken);

            if (actorMembership is null
                || actorMembership.Role is not (
                    OrganizationMembershipRole.Owner
                    or OrganizationMembershipRole.Administrator))
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return MembershipManagementResult.Failed(MembershipManagementFailureReason.ActorNotAuthorized);
            }

            var targetMembership = await _dbContext.OrganizationMemberships
                .SingleOrDefaultAsync(
                    membership => membership.Id == membershipId
                                  && membership.OrganizationId == organizationId,
                    cancellationToken);

            if (targetMembership is null)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return MembershipManagementResult.Failed(MembershipManagementFailureReason.MembershipNotFound);
            }

            var result = await mutate(actorMembership, targetMembership);
            if (!result.Success)
            {
                if (ownsTransaction)
                {
                    await transaction!.RollbackAsync(cancellationToken);
                }

                return result;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (ownsTransaction)
            {
                await transaction!.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (DbUpdateException)
        {
            if (ownsTransaction)
            {
                await transaction!.RollbackAsync(cancellationToken);
            }

            return MembershipManagementResult.Failed(MembershipManagementFailureReason.ConcurrencyConflict);
        }
        finally
        {
            if (ownsTransaction)
            {
                await transaction!.DisposeAsync();
            }
        }
    }

    private async Task<bool> IsLastActiveOwnerAsync(
        OrganizationMembership targetMembership,
        CancellationToken cancellationToken)
    {
        if (targetMembership.Role != OrganizationMembershipRole.Owner
            || targetMembership.Status != OrganizationMembershipStatus.Active)
        {
            return false;
        }

        var otherActiveOwners = await _dbContext.OrganizationMemberships.CountAsync(
            membership => membership.OrganizationId == targetMembership.OrganizationId
                          && membership.Role == OrganizationMembershipRole.Owner
                          && membership.Status == OrganizationMembershipStatus.Active
                          && membership.Id != targetMembership.Id,
            cancellationToken);

        return otherActiveOwners == 0;
    }
}
