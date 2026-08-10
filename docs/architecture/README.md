# Architecture Documentation Index

WorkshopOS v1.0.0-rc1 architecture references. Read these for design intent, boundaries, and security model.

## Foundation

| Document | Topic |
|----------|-------|
| [domain-model-v1.md](domain-model-v1.md) | Core domain overview |
| [persistence-v1.md](persistence-v1.md) | EF Core, PostgreSQL, migrations |
| [tenant-isolation-v1.md](tenant-isolation-v1.md) | Multi-tenant isolation model |
| [commercial-boundaries-v1.md](commercial-boundaries-v1.md) | Commercial workflow boundaries |

## Identity and access

| Document | Topic |
|----------|-------|
| [identity-and-membership-v1.md](identity-and-membership-v1.md) | Staff identity, org membership |
| [owner-onboarding-v1.md](owner-onboarding-v1.md) | Owner onboarding flow |
| [security-and-release-readiness-v1.md](security-and-release-readiness-v1.md) | Security baseline, auth isolation, release posture |

## Customer and vehicles

| Document | Topic |
|----------|-------|
| [customer-crm-v1.md](customer-crm-v1.md) | Customer CRM |
| [vehicle-management-v1.md](vehicle-management-v1.md) | Vehicle records |

## Workshop operations

| Document | Topic |
|----------|-------|
| [appointment-scheduling-v1.md](appointment-scheduling-v1.md) | Appointments |
| [repair-order-core-v1.md](repair-order-core-v1.md) | Repair orders |
| [workshop-operations-v1.md](workshop-operations-v1.md) | Technician assignment, operations board |
| [staff-and-team-v1.md](staff-and-team-v1.md) | Staff and team management |

## Digital vehicle inspection

| Document | Topic |
|----------|-------|
| [digital-vehicle-inspection-v1.md](digital-vehicle-inspection-v1.md) | DVI checklists and lifecycle |
| [inspection-media-v1.md](inspection-media-v1.md) | Private photo evidence |

## Estimates and customer portal

| Document | Topic |
|----------|-------|
| [estimate-and-approval-v1.md](estimate-and-approval-v1.md) | Estimates and approval |
| [customer-estimate-sharing-v1.md](customer-estimate-sharing-v1.md) | Secure share links and portal |

## Catalog, inventory, billing

| Document | Topic |
|----------|-------|
| [catalog-and-inventory-v1.md](catalog-and-inventory-v1.md) | Service/parts catalog and inventory ledger |
| [invoice-and-payment-v1.md](invoice-and-payment-v1.md) | Invoices, payments, commercial close |

## Reporting and UX

| Document | Topic |
|----------|-------|
| [reporting-and-analytics-v1.md](reporting-and-analytics-v1.md) | Read-only dashboard and KPI reports |
| [design-system-and-ux-v1.md](design-system-and-ux-v1.md) | Premium design system and UX direction |

## Setup and deployment (non-architecture)

| Document | Topic |
|----------|-------|
| [../setup/local-development.md](../setup/local-development.md) | Developer setup |
| [../deployment/production-configuration.md](../deployment/production-configuration.md) | Production configuration |
| [../deployment/deployment-runbook.md](../deployment/deployment-runbook.md) | Deployment stages |
