using System.Text.RegularExpressions;

namespace WorkshopOS.Infrastructure.IntegrationTests;

public sealed class ReleaseCandidateTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", ".."));

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath));

    private static bool RepoFileExists(string relativePath) =>
        File.Exists(Path.Combine(RepositoryRoot, relativePath));

    [Theory]
    [InlineData("docs/setup/prerequisites.md")]
    [InlineData("docs/setup/local-development.md")]
    [InlineData("docs/setup/database-migrations.md")]
    [InlineData("docs/setup/testing.md")]
    [InlineData("docs/deployment/production-configuration.md")]
    [InlineData("docs/deployment/deployment-runbook.md")]
    [InlineData("docs/deployment/security-checklist.md")]
    [InlineData("docs/deployment/backup-and-recovery.md")]
    [InlineData("docs/architecture/README.md")]
    [InlineData("docs/demo/demo-runbook.md")]
    [InlineData("docs/product/feature-matrix.md")]
    [InlineData("docs/release/RELEASE_NOTES-v1.0.0-rc1.md")]
    [InlineData("docs/release/source-package-manifest.md")]
    [InlineData("docs/release/commercial-license-decision.md")]
    [InlineData("SECURITY.md")]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    public void ReleaseDocumentation_CriticalFilesExist(string relativePath)
    {
        Assert.True(RepoFileExists(relativePath), $"Missing release documentation: {relativePath}");
    }

    [Fact]
    public void WebStartup_DoesNotAutoMigrateOrSeed()
    {
        var webSources = string.Join('\n', Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web"),
            "*.cs",
            SearchOption.AllDirectories));

        Assert.DoesNotContain("Database.Migrate", webSources, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", webSources, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureDeleted", webSources, StringComparison.Ordinal);
    }

    [Fact]
    public void BuyerFacingDocs_DoNotContainAbsoluteDeveloperPaths()
    {
        var docRoots = new[]
        {
            Path.Combine(RepositoryRoot, "docs"),
            Path.Combine(RepositoryRoot, "README.md"),
            Path.Combine(RepositoryRoot, "SECURITY.md"),
            Path.Combine(RepositoryRoot, "THIRD-PARTY-NOTICES.md"),
        };

        var absolutePathPattern = new Regex(@"/Users/|C:\\Users\\", RegexOptions.Compiled);

        foreach (var root in docRoots)
        {
            if (File.Exists(root))
            {
                Assert.DoesNotMatch(absolutePathPattern, ReadRepoFile(Path.GetRelativePath(RepositoryRoot, root)));
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                Assert.DoesNotMatch(absolutePathPattern, content);
            }
        }
    }

    [Fact]
    public void CustomerPortal_AccessView_RemainsIsolatedFromStaffLayout()
    {
        var accessView = ReadRepoFile("src/WorkshopOS.Web/Views/Portal/Access.cshtml");
        Assert.Contains("Layout = null", accessView, StringComparison.Ordinal);
        Assert.DoesNotContain("_Layout", accessView, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkshopOsStyles_DoNotReferenceRemoteGoogleFonts()
    {
        var workshopCss = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web", "wwwroot", "css"),
                "workshopos-*.css",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText);

        foreach (var css in workshopCss)
        {
            Assert.DoesNotContain("fonts.googleapis.com", css, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fonts.gstatic.com", css, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web", "wwwroot", "css", "workshopos-fonts.css")));
        Assert.True(Directory.Exists(Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web", "wwwroot", "fonts", "dm-sans")));
    }
}
