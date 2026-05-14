# Security Policy

Thank you for helping keep Equibles and its users safe.

## Supported Versions

Equibles ships from `main`. Until the first tagged release, only the
latest commit on `main` receives security fixes. Once tagged releases
exist, this section will list which version lines are supported.

| Version | Supported          |
| ------- | ------------------ |
| `main`  | :white_check_mark: |

## Reporting a Vulnerability

**Please do not file a public GitHub issue for security reports.**

Use GitHub's private vulnerability reporting to send the report
directly to the maintainers:

→ <https://github.com/daniel3303/Equibles/security/advisories/new>

Include, where possible:

- A description of the vulnerability and its impact
- Steps to reproduce or a proof-of-concept
- The affected component (e.g. `Equibles.Sec.HostedService`, MCP tool name)
- The commit SHA or Docker image tag you tested against

## What to Expect

- **Acknowledgement** within 3 business days of receiving the report.
- **Initial assessment** (severity, scope, whether a fix is needed)
  within 7 business days.
- **Coordinated disclosure** — we will agree on a disclosure timeline
  before any public discussion or fix release. Default window is up to
  90 days for severe issues, faster for low-impact ones.

## Out of Scope

- Reports against forks or third-party deployments — please contact the
  operator directly.
- Reports limited to outdated dependencies without a working exploit —
  Dependabot already covers routine bumps. Open a regular issue if a
  dependency update is overdue.
- Volumetric / DoS reports against `equibles.com` infrastructure —
  contact the site operators, not this repository.
