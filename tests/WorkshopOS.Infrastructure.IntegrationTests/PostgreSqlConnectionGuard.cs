using Npgsql;

namespace WorkshopOS.Infrastructure.IntegrationTests;

internal static class PostgreSqlConnectionGuard
{
    public static NpgsqlConnectionStringBuilder ValidateTestConnection(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:WorkshopOSTest is not configured. Set it via User Secrets or environment variables.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = builder.Host ?? "localhost";

        if (host is not "127.0.0.1" and not "localhost" and not "::1")
        {
            throw new InvalidOperationException(
                $"Integration tests may only run against a local PostgreSQL host. Found '{host}'.");
        }

        if (!string.Equals(builder.Database, "workshopos_test", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Integration tests may only run against database 'workshopos_test'. Found '{builder.Database}'.");
        }

        return builder;
    }
}
