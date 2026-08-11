using System.Text.RegularExpressions;

namespace WorkshopOS.Infrastructure.IntegrationTests;

public sealed class PortabilityTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", ".."));

    private static IEnumerable<string> EnumerateProductionCsFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories);

    [Fact]
    public void ProductionSource_DoesNotContainHardCodedDeveloperPaths()
    {
        var pattern = new Regex(@"/Users/|C:\\Users\\|/home/[^/]+/", RegexOptions.Compiled);

        foreach (var file in EnumerateProductionCsFiles())
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotMatch(pattern, content);
        }
    }

    [Fact]
    public void ProductionSource_DoesNotUsePlatformSpecificOperatingSystemChecks()
    {
        var forbidden = new[]
        {
            "OperatingSystem.IsWindows",
            "OperatingSystem.IsMacOS",
            "OperatingSystem.IsLinux",
            "Microsoft.Win32",
            "System.Drawing",
            "[DllImport",
            "LibraryImport",
        };

        foreach (var file in EnumerateProductionCsFiles())
        {
            var content = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void InspectionMedia_DefaultStorageUsesContentRootPathCombine()
    {
        var dependencyInjection = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web", "DependencyInjection.cs"));

        Assert.Contains("Path.Combine(environment.ContentRootPath, \"App_Data\", \"inspection-media\")", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void FileSystemInspectionMediaStorage_UsesPortablePathApis()
    {
        var storage = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "WorkshopOS.Infrastructure", "InspectionMedia", "FileSystemInspectionMediaStorage.cs"));

        Assert.Contains("Path.Combine", storage, StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("chmod", storage, StringComparison.Ordinal);
    }

    [Fact]
    public void RazorLayouts_DoNotReferenceRemoteFontOrCdnDependencies()
    {
        var layoutRoots = Path.Combine(RepositoryRoot, "src", "WorkshopOS.Web", "Views");
        var forbidden = new[] { "fonts.googleapis.com", "fonts.gstatic.com", "cdn.jsdelivr.net", "unpkg.com" };

        foreach (var file in Directory.EnumerateFiles(layoutRoots, "*.cshtml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void DesignTimeFactory_UsesDocumentedEnvironmentVariableName()
    {
        var factory = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src", "WorkshopOS.Infrastructure", "Persistence", "AppDbContextFactory.cs"));

        Assert.Contains("WORKSHOPOS_DESIGNTIME_CONNECTION", factory, StringComparison.Ordinal);
    }
}
