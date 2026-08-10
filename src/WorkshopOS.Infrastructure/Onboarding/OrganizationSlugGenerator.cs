using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WorkshopOS.Application.Onboarding;

namespace WorkshopOS.Infrastructure.Onboarding;

public sealed class OrganizationSlugGenerator : IOrganizationSlugGenerator
{
    private static readonly Regex InvalidSlugCharacters = new("[^a-z0-9]+", RegexOptions.CultureInvariant);

    public string GenerateSlug(string organizationName, Guid organizationId)
    {
        var normalizedBase = NormalizeBase(organizationName);
        if (string.IsNullOrEmpty(normalizedBase))
        {
            normalizedBase = "workshop";
        }

        var suffix = organizationId.ToString("N")[..8];
        var slug = $"{normalizedBase}-{suffix}";
        slug = InvalidSlugCharacters.Replace(slug.ToLowerInvariant(), "-");
        slug = CollapseHyphens(slug).Trim('-');

        if (slug.Length > 120)
        {
            slug = slug[..120].Trim('-');
        }

        return string.IsNullOrEmpty(slug) ? $"workshop-{suffix}" : slug;
    }

    private static string NormalizeBase(string input)
    {
        var trimmed = input.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSeparator = false;

        foreach (var character in trimmed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return CollapseHyphens(builder.ToString()).Trim('-');
    }

    private static string CollapseHyphens(string value) =>
        Regex.Replace(value, "-{2,}", "-", RegexOptions.CultureInvariant);
}
