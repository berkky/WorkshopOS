using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfModelDriftTests(PostgreSqlTestFixture fixture)
{
    [Fact]
    public void EFModel_HasNoPendingModelChanges()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.False(EfModelDriftVerifier.HasPendingModelChanges(context));
    }

    [Fact]
    public void EFModel_MigrationHistory_RemainsNine()
    {
        using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        Assert.Equal(9, EfModelDriftVerifier.GetMigrationCount(context));
    }
}
