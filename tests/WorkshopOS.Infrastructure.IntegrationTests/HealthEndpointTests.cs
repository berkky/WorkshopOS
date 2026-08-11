using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using WorkshopOS.Web;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class HealthEndpointTests(PostgreSqlTestFixture fixture) : IClassFixture<PostgreSqlTestFixture>
{
    [Fact]
    public async Task HealthLive_Returns200WithoutAuthentication()
    {
        await using var factory = CreateFactory(fixture.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connection", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthReady_Returns200WithValidDatabase()
    {
        await using var factory = CreateFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthEndpoints_SetCacheControlNoStore()
    {
        await using var factory = CreateFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        var readyResponse = await client.GetAsync("/health/ready");

        Assert.True(liveResponse.Headers.CacheControl?.NoStore);
        Assert.True(readyResponse.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task HealthReady_Returns503WhenDatabaseIsUnavailable()
    {
        const string brokenConnectionString =
            "Host=workshopos-health-invalid.invalid;Port=5432;Database=workshopos_unreachable;Username=invalid;Password=invalid;Timeout=1;Command Timeout=1";

        await using var factory = CreateFactory(brokenConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Unhealthy\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }

    private static ConfiguredWebApplicationFactory CreateFactory(string connectionString)
    {
        var mediaRoot = Path.Combine(Path.GetTempPath(), "workshopos-health-tests", Guid.CreateVersion7().ToString("N"));
        return new ConfiguredWebApplicationFactory(connectionString, mediaRoot);
    }

    private sealed class ConfiguredWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _mediaRoot;
        private readonly string? _previousConnectionString;
        private readonly string? _previousMediaRoot;

        public ConfiguredWebApplicationFactory(string connectionString, string mediaRoot)
        {
            _connectionString = connectionString;
            _mediaRoot = mediaRoot;
            _previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__WorkshopOS");
            _previousMediaRoot = Environment.GetEnvironmentVariable("InspectionMedia__StorageRootPath");
            Environment.SetEnvironmentVariable("ConnectionStrings__WorkshopOS", connectionString);
            Environment.SetEnvironmentVariable("InspectionMedia__StorageRootPath", mediaRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
        }

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__WorkshopOS", _previousConnectionString);
            Environment.SetEnvironmentVariable("InspectionMedia__StorageRootPath", _previousMediaRoot);
            base.Dispose(disposing);
        }
    }
}
