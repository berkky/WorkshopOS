using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Resources;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using WorkshopOS.Web.Localization;
using WorkshopOS.Web;

namespace WorkshopOS.Infrastructure.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class LocalizationTests(PostgreSqlTestFixture fixture) : IClassFixture<PostgreSqlTestFixture>
{
    private static readonly string WebRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "WorkshopOS.Web"));

    [Fact]
    public void SupportedCultures_IncludeEnglishAndTurkish()
    {
        Assert.Contains(WorkshopCultures.English, WorkshopCultures.SupportedCultureNames);
        Assert.Contains(WorkshopCultures.Turkish, WorkshopCultures.SupportedCultureNames);
        Assert.Equal(2, WorkshopCultures.SupportedCultureNames.Count);
    }

    [Fact]
    public void UnsupportedCulture_NormalizesToEnglish()
    {
        Assert.Equal(WorkshopCultures.English, WorkshopCultures.NormalizeOrDefault("fr-FR"));
        Assert.Equal(WorkshopCultures.English, WorkshopCultures.NormalizeOrDefault(null));
        Assert.Equal(WorkshopCultures.Turkish, WorkshopCultures.NormalizeOrDefault("tr-TR"));
    }

    [Fact]
    public void ResourceFiles_HaveMatchingKeys()
    {
        var enKeys = LoadResourceKeys("SharedResource.en-US.resx");
        var trKeys = LoadResourceKeys("SharedResource.tr-TR.resx");

        var missingInTr = enKeys.Except(trKeys, StringComparer.Ordinal).OrderBy(x => x).ToList();
        var missingInEn = trKeys.Except(enKeys, StringComparer.Ordinal).OrderBy(x => x).ToList();

        Assert.True(missingInTr.Count == 0, $"Keys missing in tr-TR: {string.Join(", ", missingInTr.Take(10))}");
        Assert.True(missingInEn.Count == 0, $"Keys missing in en-US: {string.Join(", ", missingInEn.Take(10))}");
    }

    [Fact]
    public async Task Landing_RepresentativeStringsDifferBetweenCultures()
    {
        Assert.NotEqual(
            LoadResourceValue("SharedResource.en-US.resx", "Landing_Hero_Eyebrow"),
            LoadResourceValue("SharedResource.tr-TR.resx", "Landing_Hero_Eyebrow"));

        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });

        var english = await client.GetStringAsync("/");
        var turkish = await GetWithCultureAsync("/", WorkshopCultures.Turkish);

        Assert.Contains("lang=\"en\"", english, StringComparison.Ordinal);
        Assert.Contains("lang=\"tr\"", turkish, StringComparison.Ordinal);
        Assert.Contains(LoadResourceValue("SharedResource.en-US.resx", "Landing_Hero_Eyebrow"), english, StringComparison.Ordinal);
        Assert.Contains(LoadResourceValue("SharedResource.tr-TR.resx", "Landing_Hero_Eyebrow"), DecodeHtml(turkish), StringComparison.Ordinal);
        Assert.DoesNotContain(">Landing_Hero_Eyebrow<", english, StringComparison.Ordinal);
        Assert.DoesNotContain(">Landing_Hero_Eyebrow<", turkish, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceManager_EmbeddedBaseName_ResolvesRepresentativeKey()
    {
        var assembly = typeof(SharedResource).Assembly;
        var manifestNames = assembly.GetManifestResourceNames();
        Assert.Contains("WorkshopOS.Web.Resources.SharedResource.resources", manifestNames);

        var rm = new ResourceManager("WorkshopOS.Web.Resources.SharedResource", assembly);
        var value = rm.GetString("Landing_Hero_Eyebrow", CultureInfo.GetCultureInfo("en-US"));

        Assert.False(string.IsNullOrEmpty(value));
        Assert.Equal(LoadResourceValue("SharedResource.en-US.resx", "Landing_Hero_Eyebrow"), value);
    }

    [Theory]
    [InlineData("en-US", "Landing_Hero_Lead")]
    [InlineData("en-US", "Layout_SignIn")]
    [InlineData("en-US", "Common_Save")]
    [InlineData("en-US", "Nav_Customers")]
    [InlineData("tr-TR", "Landing_Hero_Lead")]
    [InlineData("tr-TR", "Layout_SignIn")]
    [InlineData("tr-TR", "Common_Save")]
    [InlineData("tr-TR", "Nav_Customers")]
    public async Task StringLocalizer_ResolvesValues_NotKeys(string culture, string key)
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();
        var localized = localizer[key];

        Assert.False(localized.ResourceNotFound, $"Resource '{key}' was not found for culture '{culture}'.");
        Assert.NotEqual(key, localized.Value);
        Assert.Equal(LoadResourceValue($"SharedResource.{culture}.resx", key), localized.Value);
    }

    [Fact]
    public void Button_CtaLabels_DifferBetweenCultures()
    {
        var keys = new[]
        {
            "Nav_Customers",
            "Nav_Vehicles",
            "Nav_RepairOrders",
            "Layout_SignOut",
            "Layout_Menu",
            "Landing_Cta_SignIn",
            "Landing_Cta_Create",
            "AccessDenied_ReturnDashboard",
            "Reporting_Filter_Preset_7D",
            "Common_View",
            "Common_Filter",
            "Customers_Create",
        };

        foreach (var key in keys)
        {
            var en = LoadResourceValue("SharedResource.en-US.resx", key);
            var tr = LoadResourceValue("SharedResource.tr-TR.resx", key);

            Assert.False(string.IsNullOrWhiteSpace(en), $"Missing en-US value for {key}");
            Assert.False(string.IsNullOrWhiteSpace(tr), $"Missing tr-TR value for {key}");
            Assert.NotEqual(en, tr);
        }
    }

    [Theory]
    [InlineData("tr-TR", "Nav_Customers", "Müşteriler")]
    [InlineData("tr-TR", "Layout_SignOut", "Çıkış yap")]
    [InlineData("tr-TR", "Common_Save", "Kaydet")]
    [InlineData("en-US", "Nav_Customers", "Customers")]
    [InlineData("en-US", "Layout_SignOut", "Sign out")]
    [InlineData("en-US", "Common_Save", "Save")]
    public async Task Button_StringLocalizer_ResolvesExpectedLabel(string culture, string key, string expected)
    {
        await using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<SharedResource>>();
        var localized = localizer[key];

        Assert.False(localized.ResourceNotFound);
        Assert.Equal(expected, localized.Value);
    }

    [Fact]
    public void Views_DoNotContainHardcodedCustomerFacingButtonLabels()
    {
        var viewsRoot = Path.Combine(WebRoot, "Views");
        var buttonLabelPattern = new Regex(
            @"class\s*=\s*""[^""]*\bbtn[^""]*""[^>]*>\s*(Add|Create|Edit|Update|Delete|Save|Cancel|Close|Back|View|Details|Open|Continue|Submit|Approve|Decline|Complete|Archive|Search|Filter|Reset|Previous|Next|Start|Assign|Reassign|Pay|Send|Download|Print|Upload|Remove|Retry|Sign out|Sign in|Menu)\s*<",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(viewsRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("@Localizer", StringComparison.Ordinal) ||
                    line.Contains("Model.", StringComparison.Ordinal) ||
                    line.Contains("@Html.", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = buttonLabelPattern.Match(line);
                if (match.Success)
                {
                    offenders.Add($"{Path.GetRelativePath(WebRoot, file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Hardcoded button labels found:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public async Task CultureCookie_SubsequentRequest_UsesTurkishUiCulture()
    {
        var turkishHtml = await GetWithCultureAsync("/", WorkshopCultures.Turkish);
        Assert.Contains(LoadResourceValue("SharedResource.tr-TR.resx", "Landing_Hero_Eyebrow"), DecodeHtml(turkishHtml), StringComparison.Ordinal);
        Assert.Contains("lang=\"tr\"", turkishHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_RepresentativeStringsDifferBetweenCultures()
    {
        Assert.NotEqual(
            LoadResourceValue("SharedResource.en-US.resx", "Login_WelcomeBack"),
            LoadResourceValue("SharedResource.tr-TR.resx", "Login_WelcomeBack"));
        Assert.NotEqual(
            LoadResourceValue("SharedResource.en-US.resx", "Login_Submit"),
            LoadResourceValue("SharedResource.tr-TR.resx", "Login_Submit"));
    }

    [Fact]
    public void StatusPresentation_ResourceLabelsDifferBetweenCultures()
    {
        var en = LoadResourceValue("SharedResource.en-US.resx", "Status_RepairOrder_InProgress");
        var tr = LoadResourceValue("SharedResource.tr-TR.resx", "Status_RepairOrder_InProgress");

        Assert.Equal("In progress", en);
        Assert.Equal("Devam ediyor", tr);
    }

    [Fact]
    public async Task CulturePost_SetsCookieAndRedirectsLocally()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var getResponse = await client.GetAsync("/");
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["culture"] = WorkshopCultures.Turkish,
            ["returnUrl"] = "/account/login",
            [antiforgery.FieldName] = antiforgery.Token,
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/culture/set")
        {
            Content = new FormUrlEncodedContent(form),
        };
        postRequest.Headers.Add("Cookie", antiforgery.CookieHeader);

        var postResponse = await client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Found, postResponse.StatusCode);
        Assert.Equal("/account/login", postResponse.Headers.Location?.OriginalString);
        Assert.Contains(
            CookieRequestCultureProvider.DefaultCookieName,
            postResponse.Headers.GetValues("Set-Cookie").First(),
            StringComparison.Ordinal);
        Assert.Contains("tr-TR", postResponse.Headers.GetValues("Set-Cookie").First(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CulturePost_RejectsOpenRedirect()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var getResponse = await client.GetAsync("/");
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["culture"] = WorkshopCultures.English,
            ["returnUrl"] = "https://evil.example/phish",
            [antiforgery.FieldName] = antiforgery.Token,
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/culture/set")
        {
            Content = new FormUrlEncodedContent(form),
        };
        postRequest.Headers.Add("Cookie", antiforgery.CookieHeader);

        var postResponse = await client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Found, postResponse.StatusCode);
        Assert.Equal("/", postResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CulturePost_WithoutAntiforgery_IsRejected()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = WorkshopCultures.Turkish,
            ["returnUrl"] = "/",
        });

        var postResponse = await client.PostAsync("/culture/set", form);

        Assert.Equal(HttpStatusCode.BadRequest, postResponse.StatusCode);
    }

    [Fact]
    public async Task CulturePost_UnsupportedCulture_FallsBackToEnglishCookie()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var getResponse = await client.GetAsync("/");
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["culture"] = "de-DE",
            ["returnUrl"] = "/",
            [antiforgery.FieldName] = antiforgery.Token,
        };

        var postRequest = new HttpRequestMessage(HttpMethod.Post, "/culture/set")
        {
            Content = new FormUrlEncodedContent(form),
        };
        postRequest.Headers.Add("Cookie", antiforgery.CookieHeader);

        var postResponse = await client.SendAsync(postRequest);
        var setCookie = postResponse.Headers.GetValues("Set-Cookie").First();

        Assert.Contains("en-US", setCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("de-DE", setCookie, StringComparison.Ordinal);
    }

    private static string DecodeHtml(string html) => WebUtility.HtmlDecode(html);

    private static HashSet<string> LoadResourceKeys(string fileName)
    {
        var path = Path.Combine(WebRoot, "Resources", fileName);
        var document = System.Xml.Linq.XDocument.Load(path);
        return document
            .Descendants("data")
            .Select(x => x.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string LoadResourceValue(string fileName, string key)
    {
        var path = Path.Combine(WebRoot, "Resources", fileName);
        var document = System.Xml.Linq.XDocument.Load(path);
        var data = document.Descendants("data").FirstOrDefault(x => x.Attribute("name")?.Value == key);
        return data?.Element("value")?.Value ?? string.Empty;
    }

    private LocalizationWebApplicationFactory CreateFactory()
    {
        var mediaRoot = Path.Combine(Path.GetTempPath(), "workshopos-l10n-tests", Guid.CreateVersion7().ToString("N"));
        return new LocalizationWebApplicationFactory(fixture.ConnectionString, mediaRoot);
    }

    private async Task<string> GetWithCultureAsync(string path, string culture)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });

        var getResponse = await client.GetAsync(path);
        var antiforgery = await ExtractAntiforgeryAsync(getResponse);
        var form = new Dictionary<string, string>
        {
            ["culture"] = culture,
            ["returnUrl"] = path,
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
        return await client.GetStringAsync(path);
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

    private sealed class LocalizationWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly string _mediaRoot;
        private readonly string? _previousConnectionString;
        private readonly string? _previousMediaRoot;

        public LocalizationWebApplicationFactory(string connectionString, string mediaRoot)
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
