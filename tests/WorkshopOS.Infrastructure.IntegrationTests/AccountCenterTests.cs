using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using WorkshopOS.Domain.Organizations;
using WorkshopOS.Infrastructure.Persistence;
using WorkshopOS.Infrastructure.Tenancy;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web.Presentation;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AccountCenterTests(PostgreSqlTestFixture fixture) : IClassFixture<PostgreSqlTestFixture>
{
    private static readonly string WebRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "WorkshopOS.Web"));

    [Fact]
    public async Task Profile_Anonymous_RedirectsToLogin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/account/profile");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Profile_Authenticated_ReturnsProfileWithoutSensitiveInternals()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        var html = await client.GetStringAsync("/account/profile");

        Assert.Contains("wos-account-hero", html, StringComparison.Ordinal);
        Assert.Contains(seed.Email, html, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SecurityStamp", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.UserId.ToString(), html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_Authenticated_ShowsLocalizedLanguageSection()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        await SetCultureAsync(client, WorkshopCultures.Turkish, "/account/settings");
        var turkish = await client.GetStringAsync("/account/settings");
        Assert.Contains("Dil", turkish, StringComparison.Ordinal);

        await SetCultureAsync(client, WorkshopCultures.English, "/account/settings");
        var english = await client.GetStringAsync("/account/settings");
        Assert.Contains("Language", english, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_ChangePassword_RequiresAntiforgery()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });

        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);
        var login = await PostLoginRequestAsync(client, seed.Email, IdentityTestServiceFactory.ValidTestPassword);
        await client.GetAsync(login.Headers.Location);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPassword"] = IdentityTestServiceFactory.ValidTestPassword,
            ["NewPassword"] = "NewValidPass123!",
            ["ConfirmPassword"] = "NewValidPass123!",
        });

        var response = await client.PostAsync("/account/security", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Security_ChangePassword_InvalidCurrentPassword_ShowsSafeError()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        var response = await PostSecurityAsync(
            client,
            "WrongPass123!",
            "NewValidPass123!",
            "NewValidPass123!");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The current password is incorrect.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(IdentityTestServiceFactory.ValidTestPassword, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppShell_ContainsAccountMenuContract()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        await SetCultureAsync(client, WorkshopCultures.Turkish, "/operations");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/operations"));
        Assert.Contains("wos-account-center", html, StringComparison.Ordinal);
        Assert.Contains("wos-account-trigger", html, StringComparison.Ordinal);
        Assert.Contains("wos-account-avatar", html, StringComparison.Ordinal);
        Assert.Contains("data-wos-account-menu", html, StringComparison.Ordinal);
        Assert.Contains("/account/profile", html, StringComparison.Ordinal);
        Assert.Contains("/account/settings", html, StringComparison.Ordinal);
        Assert.Contains("/account/security", html, StringComparison.Ordinal);
        Assert.Contains("Profilim", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wos-sidebar-user", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wos-sidebar-user-label", html, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/account/logout", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountRoutes_Authenticated_ReturnHtmlPages()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        foreach (var path in new[] { "/account/profile", "/account/settings", "/account/security" })
        {
            var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("attachment", response.Content.Headers.ContentDisposition?.DispositionType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("wos-page-header", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OperationsFilter_RenderedShell_UsesResponsiveGridAndLocalizedLabels()
    {
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var seed = await SeedCommittedLoginUserAsync(suffix);

        await using var factory = CreateFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, seed.Email);

        await SetCultureAsync(client, WorkshopCultures.Turkish, "/operations");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/operations"));

        Assert.Contains("wos-operations-filter-grid", html, StringComparison.Ordinal);
        Assert.Contains("Tüm lokasyonlar", html, StringComparison.Ordinal);
        Assert.Contains("Tüm durumlar", html, StringComparison.Ordinal);
        Assert.Contains("Tüm öncelikler", html, StringComparison.Ordinal);
        Assert.Contains("Tüm teknisyenler", html, StringComparison.Ordinal);
        Assert.Contains("wos-filter-checkbox", html, StringComparison.Ordinal);
    }

    [Fact]
    public void UiFormatting_AllLocations_AndDateRanges_AreCultureAware()
    {
        var en = LoadResourceValue("SharedResource.en-US.resx", "Common_AllLocations");
        var tr = LoadResourceValue("SharedResource.tr-TR.resx", "Common_AllLocations");
        Assert.Equal("All locations", en);
        Assert.Equal("Tüm lokasyonlar", tr);

        using var scope = CultureInfo.CurrentCulture.Name == "en-US"
            ? new CultureSwap("en-US")
            : new CultureSwap("en-US");
        var enFormatter = CreateFormatter("en-US");
        var enRange = enFormatter.FormatDateRange(new DateOnly(2026, 7, 13), new DateOnly(2026, 8, 11));
        Assert.Contains("Jul 13, 2026", enRange, StringComparison.Ordinal);
        Assert.Contains("Aug 11, 2026", enRange, StringComparison.Ordinal);

        using var trScope = new CultureSwap("tr-TR");
        var trFormatter = CreateFormatter("tr-TR");
        var trRange = trFormatter.FormatDateRange(new DateOnly(2026, 7, 13), new DateOnly(2026, 8, 11));
        Assert.Contains("13", trRange, StringComparison.Ordinal);
        Assert.Contains("Tem", trRange, StringComparison.Ordinal);
        Assert.Contains("11", trRange, StringComparison.Ordinal);
        Assert.Contains("Ağu", trRange, StringComparison.Ordinal);
        Assert.DoesNotContain("Jul", trRange, StringComparison.Ordinal);
    }

    [Fact]
    public void UiFormatting_TurkishDateAndDateTime_AreNatural()
    {
        using var scope = new CultureSwap("tr-TR");
        var formatter = CreateFormatter("tr-TR");
        var utc = new DateTimeOffset(2026, 8, 11, 17, 25, 0, TimeSpan.Zero);

        Assert.Equal("11 Ağu 2026", formatter.FormatDate(new DateOnly(2026, 8, 11)));
        Assert.Equal("11 Ağu 2026 · 20:25", formatter.FormatDateTime(utc, "Europe/Istanbul"));
    }

    [Fact]
    public void UiFormatting_EnglishDateAndDateTime_AreNatural()
    {
        using var scope = new CultureSwap("en-US");
        var formatter = CreateFormatter("en-US");
        var utc = new DateTimeOffset(2026, 8, 11, 17, 25, 0, TimeSpan.Zero);

        Assert.Equal("Aug 11, 2026", formatter.FormatDate(new DateOnly(2026, 8, 11)));
        Assert.Equal("Aug 11, 2026 · 8:25 PM", formatter.FormatDateTime(utc, "Europe/Istanbul"));
    }

    [Fact]
    public void UiFormatting_TimeZoneConversion_DoesNotMutateUtcValue_AndReportsUseFormatter()
    {
        using var scope = new CultureSwap("tr-TR");
        var formatter = CreateFormatter("tr-TR");
        var utc = new DateTimeOffset(2026, 8, 11, 17, 25, 0, TimeSpan.Zero);
        var original = utc;

        var display = formatter.FormatGeneratedAt(utc, "Europe/Istanbul");

        Assert.Equal("11 Ağu 2026 · 20:25", display);
        Assert.Equal(original, utc);
        Assert.Equal(TimeSpan.Zero, utc.Offset);

        foreach (var reportView in new[] { "Operations.cshtml", "Commercial.cshtml" })
        {
            var razor = File.ReadAllText(Path.Combine(WebRoot, "Views", "Reports", reportView));
            Assert.Contains("UiFormatting.FormatGeneratedAt", razor, StringComparison.Ordinal);
            Assert.DoesNotContain("ToString(\"u\")", razor, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FormSelect_UsesSingleChevronContract()
    {
        var css = File.ReadAllText(Path.Combine(WebRoot, "wwwroot/css/workshopos-components.css"));
        Assert.Contains("--bs-form-select-bg-img: none", css, StringComparison.Ordinal);
        Assert.Contains("appearance: none", css, StringComparison.Ordinal);
        Assert.Contains("background-repeat: no-repeat", css, StringComparison.Ordinal);
        Assert.Contains("background-size: 16px 12px", css, StringComparison.Ordinal);
    }

    private static WorkshopUiFormatting CreateFormatter(string culture)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        return new WorkshopUiFormatting(new TestStringLocalizer(LoadResources(culture)));
    }

    private static Dictionary<string, string> LoadResources(string culture)
    {
        var path = Path.Combine(WebRoot, "Resources", $"SharedResource.{culture}.resx");
        var document = System.Xml.Linq.XDocument.Load(path);
        return document.Descendants("data")
            .ToDictionary(
                x => x.Attribute("name")!.Value,
                x => x.Element("value")!.Value);
    }

    private sealed class TestStringLocalizer(Dictionary<string, string> values) : Microsoft.Extensions.Localization.IStringLocalizer<WorkshopOS.Web.SharedResource>
    {
        public Microsoft.Extensions.Localization.LocalizedString this[string name] =>
            new(name, values.TryGetValue(name, out var value) ? value : name);

        public Microsoft.Extensions.Localization.LocalizedString this[string name, params object[] arguments] => this[name];

        public IEnumerable<Microsoft.Extensions.Localization.LocalizedString> GetAllStrings(bool includeParentCultures) =>
            values.Select(pair => new Microsoft.Extensions.Localization.LocalizedString(pair.Key, pair.Value));

        public Microsoft.Extensions.Localization.IStringLocalizer WithCulture(CultureInfo culture) => this;
    }

    private sealed class CultureSwap : IDisposable
    {
        private readonly CultureInfo _previousCulture;
        private readonly CultureInfo _previousUiCulture;

        public CultureSwap(string culture)
        {
            _previousCulture = CultureInfo.CurrentCulture;
            _previousUiCulture = CultureInfo.CurrentUICulture;
            var info = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = info;
            CultureInfo.CurrentUICulture = info;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previousCulture;
            CultureInfo.CurrentUICulture = _previousUiCulture;
        }
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

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> factory,
        string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });

        var login = await PostLoginRequestAsync(client, email, IdentityTestServiceFactory.ValidTestPassword);
        await client.GetAsync(login.Headers.Location);
        return client;
    }

    private static async Task<HttpResponseMessage> PostSecurityAsync(
        HttpClient client,
        string currentPassword,
        string newPassword,
        string confirmPassword)
    {
        var getResponse = await client.GetAsync("/account/security");
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["CurrentPassword"] = currentPassword,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = confirmPassword,
            [antiforgery.FieldName] = antiforgery.Token,
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/account/security")
        {
            Content = new FormUrlEncodedContent(form),
        };
        postRequest.Headers.TryAddWithoutValidation("Cookie", antiforgery.CookieHeader);
        return await client.SendAsync(postRequest);
    }

    private static async Task<HttpResponseMessage> PostLoginRequestAsync(HttpClient client, string email, string password)
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
        postRequest.Headers.TryAddWithoutValidation("Cookie", antiforgery.CookieHeader);
        await client.SendAsync(postRequest);
    }

    private static async Task<(string FieldName, string Token, string CookieHeader)> ExtractAntiforgeryAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(html, @"name=""(__RequestVerificationToken)""[^>]*value=""([^""]+)""");
        Assert.True(tokenMatch.Success);
        var cookieHeader = response.Headers.TryGetValues("Set-Cookie", out var rawCookies)
            ? string.Join("; ", rawCookies.Select(c => c.Split(';')[0]))
            : string.Empty;
        return (tokenMatch.Groups[1].Value, tokenMatch.Groups[2].Value, cookieHeader);
    }

    private static string LoadResourceValue(string fileName, string key)
    {
        var path = Path.Combine(WebRoot, "Resources", fileName);
        var document = System.Xml.Linq.XDocument.Load(path);
        return document.Descendants("data").First(x => x.Attribute("name")!.Value == key).Element("value")!.Value;
    }

    private AccountWebApplicationFactory CreateFactory()
    {
        var mediaRoot = Path.Combine(Path.GetTempPath(), "workshopos-account-tests", Guid.CreateVersion7().ToString("N"));
        return new AccountWebApplicationFactory(fixture.ConnectionString, mediaRoot);
    }

    private sealed class CommittedLoginUser : IAsyncDisposable
    {
        private readonly PostgreSqlTestFixture _fixture;
        private readonly Guid _userId;
        private readonly Guid _organizationId;

        public CommittedLoginUser(PostgreSqlTestFixture fixture, Guid userId, Guid organizationId, string email)
        {
            _fixture = fixture;
            _userId = userId;
            _organizationId = organizationId;
            Email = email;
            UserId = userId;
        }

        public string Email { get; }
        public Guid UserId { get; }

        public async ValueTask DisposeAsync()
        {
            await using var context = _fixture.CreateContext(new UnresolvedOrganizationContext(), TimeProvider.System);
            var memberships = await context.OrganizationMemberships
                .Where(m => m.UserId == _userId || m.OrganizationId == _organizationId)
                .ToListAsync();
            context.OrganizationMemberships.RemoveRange(memberships);
            var organization = await context.Organizations.FindAsync(_organizationId);
            if (organization is not null) context.Organizations.Remove(organization);
            var user = await context.Users.FindAsync(_userId);
            if (user is not null) context.Users.Remove(user);
            await context.SaveChangesAsync();
        }
    }

    private sealed class AccountWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _mediaRoot;
        private readonly string? _previousConnectionString;
        private readonly string? _previousMediaRoot;

        public AccountWebApplicationFactory(string connectionString, string mediaRoot)
        {
            _connectionString = connectionString;
            _mediaRoot = mediaRoot;
            _previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__WorkshopOS");
            _previousMediaRoot = Environment.GetEnvironmentVariable("InspectionMedia__StorageRootPath");
            Environment.SetEnvironmentVariable("ConnectionStrings__WorkshopOS", connectionString);
            Environment.SetEnvironmentVariable("InspectionMedia__StorageRootPath", mediaRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(Environments.Development);

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__WorkshopOS", _previousConnectionString);
            Environment.SetEnvironmentVariable("InspectionMedia__StorageRootPath", _previousMediaRoot);
            base.Dispose(disposing);
        }
    }
}
