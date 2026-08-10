# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| v1.0.0-rc1 (technical release candidate) | Security fixes by maintainer discretion until formal release |

## Reporting a vulnerability

WorkshopOS handles security reports through **responsible disclosure**.

**Before public distribution:** configure a real security contact for your organization.

### Owner action required

Replace this placeholder workflow with your production process:

1. Choose a monitored inbox using your official domain when available (preferred pattern: `security@<official-workshopos-domain>`) or a bug-bounty program URL.
2. Add it to this file and to customer-facing documentation.
3. Ensure the contact is staffed and can respond to reports.

**Current repository status:** no security contact email is defined. Do not send reports to placeholder or guessed addresses.

### What to include

- Description of the issue and impact
- Steps to reproduce
- Affected version (e.g. v1.0.0-rc1)
- Any proof-of-concept (non-destructive)

### What not to include

- Production database credentials
- Live customer data
- Raw estimate share tokens

## Security baseline

See [docs/architecture/security-and-release-readiness-v1.md](docs/architecture/security-and-release-readiness-v1.md) for the implemented controls baseline.

## Out of scope for reports

- Missing features listed in [docs/product/current-limitations.md](docs/product/current-limitations.md)
- Deployment misconfiguration (exposed secrets, missing HTTPS) on operator infrastructure
- Social engineering

## Compliance

WorkshopOS does **not** claim PCI DSS, SOC 2, ISO 27001, HIPAA, GDPR, or KVKK certification.

## Updates

Security-relevant dependency updates should be tracked via:

```bash
dotnet list WorkshopOS.slnx package --vulnerable --include-transitive
```
