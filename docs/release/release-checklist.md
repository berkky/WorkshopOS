# Release Checklist — v1.0.0-rc1

## SOURCE RC COMPLETE (technical)

- [ ] Debug build: 0 errors, 0 warnings
- [ ] Release build: 0 errors, 0 warnings
- [ ] Release `-warnaserror`: pass
- [ ] Full test suite: all pass (657 at RC)
- [ ] EF: 9 migrations, 0 pending, no model drift
- [ ] Vulnerability audit: no known vulnerable packages
- [ ] Secret audit: no real credentials in source/package
- [ ] Clean source snapshot: restore, build, publish pass
- [ ] RC ZIP: created, SHA-256 recorded, content audit pass
- [ ] Extracted package: build and publish pass
- [ ] Documentation: setup, deployment, demo, architecture index
- [ ] THIRD-PARTY-NOTICES.md complete
- [ ] SECURITY.md present
- [ ] No startup migration/seed
- [ ] Git: no commit/tag required for technical RC (working tree)

## PRODUCTION DEPLOYMENT REQUIRED

- [ ] Production PostgreSQL provisioned
- [ ] Connection string in secret manager
- [ ] Migrations applied explicitly
- [ ] HTTPS hostname and TLS certificates
- [ ] Trusted reverse proxy / forwarded headers configured
- [ ] Data Protection persistent key ring
- [ ] Private media persistent storage + permissions
- [ ] Backup jobs (DB + media + DP keys)
- [ ] Log destination configured

## POST-DEPLOY VERIFICATION

- [ ] Smoke routes (public, staff challenge, portal challenge)
- [ ] Owner onboarding in production (or pre-provisioned org)
- [ ] Rate limiting effective behind proxy
- [ ] Security headers present
- [ ] Manual visual QA (see [manual-visual-qa.md](manual-visual-qa.md))
- [ ] Dependency vulnerability scan in CI/CD

## COMMERCIAL DISTRIBUTION (owner decisions)

- [x] Commercial direction selected: **Proprietary Commercial Source License**
- [ ] Term sheet / decision matrix reviewed by counsel
- [ ] Final project license agreement / `LICENSE` published
- [ ] Security contact email configured (see SECURITY.md)
- [ ] Release commit/tag created (separate approval — STEP 25)
- [ ] Native Windows verification (if required by market)

## Status labels

| Label | Meaning |
|-------|---------|
| **Technical RC READY** | Source package verified; not production-deployed |
| **Production deployed** | Only after real deployment checklist complete |

Do not conflate technical RC with production go-live.
