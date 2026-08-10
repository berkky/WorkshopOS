# Design System and UX v1 (STEP 20)

STEP 20 is a presentation-layer refinement. No domain, database, or business-rule changes.

## CSS organization

| File | Purpose |
|------|---------|
| `workshopos-tokens.css` | Design tokens (colors, spacing, radii, shadows) |
| `workshopos-components.css` | Reusable UI components (cards, badges, tables, board, calendar) |
| `workshopos-shell.css` | Authenticated application shell (sidebar, topbar, mobile offcanvas) |
| `workshopos-public.css` | Public landing, auth, and customer portal shared styles |
| `site.css` | Minimal global overrides |

Bootstrap remains the core framework. No new CSS framework, npm toolchain, or chart library.

## Internal application shell

Authenticated users (except Home/Account/Onboarding public controllers) use `_AppShell.cshtml`:

- Desktop: persistent sidebar navigation with policy-aware links
- Mobile: top bar + Bootstrap offcanvas menu
- Skip-to-main-content link
- Sign out via existing POST logout endpoint

Navigation hiding is convenience only; server authorization policies remain authoritative.

## Customer portal isolation

`_CustomerPortalLayout.cshtml` is separate from the internal shell. No internal navigation, no reporting links, no inspection media references.

## Status presentation

`StatusPresentation` maps domain enums to labeled badge CSS classes. Status is never conveyed by color alone.

## Forms and actions

- Primary actions use `btn-wos-primary`
- Destructive domain actions retain explicit button copy (issue invoice, void, commercial close, etc.)
- `site.js` optionally disables submit buttons after valid POST to reduce double-submit (server remains authoritative)

## Responsive strategy

- CSS grid/flex with Bootstrap breakpoints
- Tables wrapped in `wos-table-wrap` for horizontal scroll on narrow screens
- Operations board uses responsive grid columns
- Appointment calendar stacks on tablet/mobile

## Accessibility principles

- Visible labels on inputs
- `:focus-visible` ring via design tokens
- Skip link on major layouts
- Semantic landmarks (`main`, `nav`, headings)
- No formal WCAG certification claimed in STEP 20

## Visual QA limitation

Automated tests cover layout isolation, antiforgery presence, asset existence, and GET-only reporting routes. **Independent browser visual QA at multiple breakpoints is recommended before release** (STEP 21).

## Security boundaries preserved

- Antiforgery on mutations
- Customer portal auth scheme isolation
- Private inspection media route
- Secure share token one-time display (no token hash in markup)
- No `Html.Raw` for user content
