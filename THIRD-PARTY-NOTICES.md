# Third-Party Notices

WorkshopOS v1.0.0-rc1 — informational summary of third-party components. **This file is not legal advice.** Consult qualified counsel for license compliance in your distribution model.

## NuGet packages (direct)

| Component | Version | License | Source |
|-----------|---------|---------|--------|
| Microsoft.AspNetCore.Authorization | 10.0.10 | MIT | [.NET](https://github.com/dotnet/aspnetcore) |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.10 | MIT | [.NET](https://github.com/dotnet/aspnetcore) |
| Microsoft.EntityFrameworkCore | 10.0.10 | MIT | [.NET](https://github.com/dotnet/efcore) |
| Microsoft.EntityFrameworkCore.Design | 10.0.10 | MIT | [.NET](https://github.com/dotnet/efcore) |
| Microsoft.EntityFrameworkCore.Relational | 10.0.10 | MIT | [.NET](https://github.com/dotnet/efcore) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL License | [Npgsql EF](https://github.com/npgsql/efcore.pg) |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | [Microsoft.TestPlatform](https://github.com/microsoft/testplatform) |
| xunit | 2.9.3 | Apache-2.0 | [xUnit](https://github.com/xunit/xunit) |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | [xUnit](https://github.com/xunit/visualstudio.xunit) |

## NuGet packages (significant transitive)

| Component | Version | License | Notes |
|-----------|---------|---------|-------|
| Npgsql | 10.0.3 | PostgreSQL License | PostgreSQL ADO.NET driver |
| Newtonsoft.Json | 13.0.3 | MIT | Test/design-time tooling dependency |
| Humanizer.Core | 2.14.1 | MIT | EF Core design-time |
| Microsoft.CodeAnalysis.* | 5.0.0 | MIT | EF Core design-time analyzers |

Full transitive graph: `dotnet list WorkshopOS.slnx package --include-transitive`

.NET runtime and ASP.NET Core components are subject to the [.NET license](https://github.com/dotnet/core/blob/main/LICENSE.txt) (MIT).

## Bundled front-end libraries (wwwroot/lib)

| Component | Version | License | Bundled license file |
|-----------|---------|---------|----------------------|
| Bootstrap | 5.3.3 | MIT | `src/WorkshopOS.Web/wwwroot/lib/bootstrap/LICENSE` |
| jQuery | 3.7.1 | MIT | `src/WorkshopOS.Web/wwwroot/lib/jquery/LICENSE.txt` |
| jQuery Validation | 1.21.0 | MIT | `src/WorkshopOS.Web/wwwroot/lib/jquery-validation/LICENSE.md` |
| jQuery Validation Unobtrusive | (bundled with ASP.NET template) | MIT | `src/WorkshopOS.Web/wwwroot/lib/jquery-validation-unobtrusive/LICENSE.txt` |

## Self-hosted typography (wwwroot/fonts)

| Component | Delivery | License | Notes |
|-----------|----------|---------|-------|
| DM Sans | Local TTF files in `wwwroot/fonts/dm-sans/` | SIL Open Font License 1.1 | Weights 400–700 + italic 400; see `OFL.txt` in font directory. Loaded via `workshopos-fonts.css`. **No runtime Google Fonts request.** |

## Original WorkshopOS assets

| Asset | Notes |
|-------|-------|
| `hero-vehicle-wireframe.svg` | Original/generic automotive wireframe artwork |
| Design system CSS/JS | WorkshopOS-authored |
| `workshopos-hero.js` | WorkshopOS-authored |

No third-party automotive manufacturer logos or stock photography dependencies identified in product assets.

## WorkshopOS project license

**Proprietary Commercial Source License** direction selected by owner. **Final legal instrument not yet published** in this repository. Third-party notices above do not grant rights to WorkshopOS source code itself. See [docs/release/proprietary-commercial-license-term-sheet.md](docs/release/proprietary-commercial-license-term-sheet.md) and [docs/release/legal-review-pack.md](docs/release/legal-review-pack.md).
