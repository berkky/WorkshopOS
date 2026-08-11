using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Localization;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class LoginRegressionTests(PostgreSqlTestFixture fixture) : IClassFixture<PostgreSqlTestFixture>
{
    [Fact]
    public async Task LoginGet_Returns200_WithPostFormTargetingAccountLogin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/account/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("action=\"/account/login\"", html, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Email\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Password\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"RememberMe\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"ReturnUrl\"", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPage_DoesNotNestLanguageSwitcherInsideLoginForm()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/account/login");
        var actionIndex = html.IndexOf("action=\"/account/login\"", StringComparison.Ordinal);
        Assert.True(actionIndex >= 0);
        var loginFormStart = html.LastIndexOf("<form", actionIndex, StringComparison.OrdinalIgnoreCase);
        Assert.True(loginFormStart >= 0);

        var beforeLogin = html[..loginFormStart];
        var openCount = Regex.Matches(beforeLogin, "<form\\b", RegexOptions.IgnoreCase).Count;
        var closeCount = Regex.Matches(beforeLogin, "</form>", RegexOptions.IgnoreCase).Count;
        Assert.Equal(openCount, closeCount);
    }

    [Fact]
    public async Task LoginGet_WhenAuthenticated_RedirectsToOperations()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginResponse = await PostLoginRequestAsync(
            client,
            seed.Email,
            IdentityTestServiceFactory.ValidTestPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        await client.GetAsync(loginResponse.Headers.Location);

        var authenticatedLoginGet = await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.Redirect, authenticatedLoginGet.StatusCode);
        Assert.Equal("/operations", authenticatedLoginGet.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginPost_InvalidCredentials_ShowsLocalizedGenericFailure()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var response = await PostLoginRequestAsync(client, "missing-user@example.com", "WrongPass123!");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("wos-inline-validation", html, StringComparison.Ordinal);
        Assert.Contains("validation-summary-errors", html, StringComparison.Ordinal);
        Assert.Contains("Invalid email or password.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPost_InvalidCredentials_TurkishCulture_ShowsLocalizedGenericFailure()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        await SetCultureAsync(client, WorkshopCultures.Turkish, "/account/login");
        var response = await PostLoginRequestAsync(client, "missing-user@example.com", "WrongPass123!");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("wos-inline-validation", html, StringComparison.Ordinal);
        Assert.Contains("validation-summary-errors", html, StringComparison.Ordinal);
        Assert.Contains("E-posta veya şifre hatalı.", DecodeHtml(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPost_ValidCredentials_RedirectsAndIssuesAuthCookie()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var response = await PostLoginRequestAsync(
            client,
            seed.Email,
            IdentityTestServiceFactory.ValidTestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/operations", response.Headers.Location?.OriginalString);
        Assert.Contains(
            "WorkshopOS.Auth",
            response.Headers.GetValues("Set-Cookie").First(),
            StringComparison.Ordinal);

        using var followUp = await client.GetAsync(response.Headers.Location);
        Assert.Equal("/operations", followUp.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains("wos-app-body", await followUp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginPost_ValidCredentials_CultureSwitchDoesNotInvalidateSession()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var loginResponse = await PostLoginRequestAsync(
            client,
            seed.Email,
            IdentityTestServiceFactory.ValidTestPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/operations", loginResponse.Headers.Location?.OriginalString);
        await client.GetAsync(loginResponse.Headers.Location);

        await SetCultureAsync(client, WorkshopCultures.Turkish, "/operations");
        var turkishOperations = await client.GetStringAsync("/operations");
        Assert.Contains("lang=\"tr\"", turkishOperations, StringComparison.Ordinal);
        Assert.Contains("Çıkış yap", DecodeHtml(turkishOperations), StringComparison.Ordinal);
        Assert.Contains("Filtrele", DecodeHtml(turkishOperations), StringComparison.Ordinal);
        Assert.DoesNotContain(">Sign out<", turkishOperations, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Filter<", turkishOperations, StringComparison.OrdinalIgnoreCase);

        var turkishCustomers = await client.GetStringAsync("/customers");
        Assert.Contains("Müşteri oluştur", DecodeHtml(turkishCustomers), StringComparison.Ordinal);

        var turkishVehicles = await client.GetStringAsync("/vehicles");
        Assert.Contains("Araç oluştur", DecodeHtml(turkishVehicles), StringComparison.Ordinal);

        var turkishRepairOrders = await client.GetStringAsync("/repair-orders");
        Assert.Contains("İş emri oluştur", DecodeHtml(turkishRepairOrders), StringComparison.Ordinal);

        var turkishEstimates = await client.GetStringAsync("/estimates");
        Assert.Contains("Filtrele", DecodeHtml(turkishEstimates), StringComparison.Ordinal);

        var turkishInventory = await client.GetStringAsync("/inventory");
        Assert.Contains("Stok düzelt", DecodeHtml(turkishInventory), StringComparison.Ordinal);

        var turkishInvoices = await client.GetStringAsync("/invoices");
        Assert.Contains("Filtrele", DecodeHtml(turkishInvoices), StringComparison.Ordinal);

        await SetCultureAsync(client, WorkshopCultures.English, "/operations");
        var englishOperations = await client.GetStringAsync("/operations");
        Assert.Contains("lang=\"en\"", englishOperations, StringComparison.Ordinal);
        Assert.Contains("Sign out", englishOperations, StringComparison.Ordinal);
        Assert.Contains("Filter", englishOperations, StringComparison.Ordinal);
        Assert.DoesNotContain(">Çıkış yap<", englishOperations, StringComparison.Ordinal);

        var protectedResponse = await client.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    private async Task<CommittedLoginUser> SeedCommittedLoginUserAsync(string suffix)
    {
        var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
        var user = await TestDataFactory.PersistUserAsync(context, suffix);
        var organization = await TestDataFactory.PersistOrganizationAsync(context, suffix);
        await TestDataFactory.PersistMembershipAsync(
            context,
            organization.Id,
            user.Id,
            OrganizationMembershipRole.Owner);
        await context.SaveChangesAsync();
        await context.DisposeAsync();

        return new CommittedLoginUser(fixture, user.Id, organization.Id, user.Email!);
    }

    private sealed class CommittedLoginUser(
        PostgreSqlTestFixture fixture,
        Guid userId,
        Guid organizationId,
        string email) : IAsyncDisposable
    {
        public string Email { get; } = email;

        public async ValueTask DisposeAsync()
        {
            await using var context = fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
            await CleanupCommittedLoginUserAsync(context, userId, organizationId);
        }
    }

    private static async Task CleanupCommittedLoginUserAsync(
        AppDbContext context,
        Guid userId,
        Guid organizationId)
    {
        var memberships = await context.OrganizationMemberships
            .Where(membership => membership.UserId == userId || membership.OrganizationId == organizationId)
            .ToListAsync();
        context.OrganizationMemberships.RemoveRange(memberships);

        var organization = await context.Organizations.FindAsync(organizationId);
        if (organization is not null)
        {
            context.Organizations.Remove(organization);
        }

        var user = await context.Users.FindAsync(userId);
        if (user is not null)
        {
            context.Users.Remove(user);
        }

        await context.SaveChangesAsync();
    }

    private LoginWebApplicationFactory CreateFactory()
    {
        var mediaRoot = Path.Combine(Path.GetTempPath(), "workshopos-login-tests", Guid.CreateVersion7().ToString("N"));
        return new LoginWebApplicationFactory(fixture.ConnectionString, mediaRoot);
    }

    private static async Task<HttpResponseMessage> PostLoginRequestAsync(
        HttpClient client,
        string email,
        string password)
    {
        var getResponse = await client.GetAsync("/account/login");
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["RememberMe"] = "false",
            [antiforgery.FieldName] = antiforgery.Token,
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/account/login")
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (!string.IsNullOrEmpty(antiforgery.CookieHeader))
        {
            postRequest.Headers.TryAddWithoutValidation("Cookie", antiforgery.CookieHeader);
        }

        return await client.SendAsync(postRequest);
    }

    private static async Task SetCultureAsync(HttpClient client, string culture, string returnUrl)
    {
        var getResponse = await client.GetAsync(returnUrl);
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["culture"] = culture,
            ["returnUrl"] = returnUrl,
            [antiforgery.FieldName] = antiforgery.Token,
        };

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/culture/set")
        {
            Content = new FormUrlEncodedContent(form),
        };

        if (!string.IsNullOrEmpty(antiforgery.CookieHeader))
        {
            postRequest.Headers.TryAddWithoutValidation("Cookie", antiforgery.CookieHeader);
        }

        await client.SendAsync(postRequest);
    }

    private static async Task<(string FieldName, string Token, string CookieHeader)> ExtractAntiforgeryAsync(
        HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(html, @"name=""(__RequestVerificationToken)""[^>]*value=""([^""]+)""");
        Assert.True(tokenMatch.Success, "Antiforgery token not found in response HTML.");

        var cookieHeader = response.Headers.TryGetValues("Set-Cookie", out var rawCookies)
            ? string.Join("; ", rawCookies.Select(c => c.Split(';')[0]))
            : string.Empty;

        return (tokenMatch.Groups[1].Value, tokenMatch.Groups[2].Value, cookieHeader);
    }

    private static string DecodeHtml(string html) => WebUtility.HtmlDecode(html);

    private sealed class LoginWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _mediaRoot;
        private readonly string? _previousConnectionString;
        private readonly string? _previousMediaRoot;

        public LoginWebApplicationFactory(string connectionString, string mediaRoot)
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
