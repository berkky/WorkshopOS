using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using Xunit;

namespace WorkshopOS.Infrastructure.IntegrationTests;

public sealed class PostgreSqlTestFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PostgreSqlTestFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("WorkshopOSTest");
        PostgreSqlConnectionGuard.ValidateTestConnection(connectionString);
        ConnectionString = connectionString!;

        await using var context = CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public AppDbContext CreateContext(IOrganizationContext organizationContext, TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options, organizationContext, timeProvider);
    }

    public async Task<DatabaseTransactionScope> BeginScopeAsync(
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;

        var context = new AppDbContext(options, organizationContext, timeProvider ?? TimeProvider.System);
        await context.Database.UseTransactionAsync(transaction);

        return new DatabaseTransactionScope(context, connection, transaction);
    }
}

public sealed class DatabaseTransactionScope : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public DatabaseTransactionScope(
        AppDbContext context,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        Context = context;
        _connection = connection;
        _transaction = transaction;
    }

    public AppDbContext Context { get; }

    public AppDbContext CreateContext(
        IOrganizationContext organizationContext,
        TimeProvider? timeProvider = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connection)
            .Options;

        var context = new AppDbContext(options, organizationContext, timeProvider ?? TimeProvider.System);
        context.Database.UseTransaction(_transaction);
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

[CollectionDefinition(PostgreSqlCollection.Name)]
public sealed class PostgreSqlTestCollectionDefinition : ICollectionFixture<PostgreSqlTestFixture>
{
}

public static class PostgreSqlCollection
{
    public const string Name = "PostgreSQL";
}
