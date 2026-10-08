# Branch protection for `main`

## A real constraint while this repo is private

On GitHub Free, a ruleset's rules only *enforce* on **public** repositories. A private
repo under a Free account can have a ruleset configured in the UI, but it won't actually
block anything -- this is a documented GitHub limitation, not a bug in this setup.

Decided for this project: stay on the Free plan, create both rulesets below now in
**Evaluate** (dry-run) mode, and treat "no direct pushes to `main`" as a personal-
discipline convention until the repo goes public -- at which point enforcement becomes
real for free, with no plan change needed. (The alternative, upgrading to GitHub Pro
now for immediate real enforcement, was considered and explicitly declined.)

## Two rulesets, not one -- and why

A single ruleset's bypass list applies to *every* rule inside it. Splitting into two
keeps "I can force-push if truly necessary" from silently also granting "I can skip code
review":

### Ruleset 1: `require-pr-review`

- Target: `main`.
- Rule: **Require a pull request before merging** (≥1 approving review, dismiss stale
  approvals on new commits, require review from Code Owners).
- Required status checks: `Python (lint + tests + coverage)`, `.NET (build + tests +
  coverage)`, `PowerShell (PSScriptAnalyzer)` (job names from `.github/workflows/ci.yml`),
  `dotnet format`, `ruff format` (from `format.yml`), `Secret scan`, `Vulnerability scan`
  (from `security.yml`). Require branches to be up to date before merging.
- **Bypass list: empty.** With no bypass actor, nobody -- including the repo owner --
  can skip this rule via a bypass grant. The only way around it is deliberately editing
  or disabling the ruleset itself, a conscious two-step action, never an accidental push.

### Ruleset 2: `protect-branch-integrity`

- Target: `main`.
- Rules: **Block force pushes**, **Restrict deletions**.
- **Bypass list: the "Repository admin" role, bypass mode "Always."** This is the
  narrow, explicit override the owner asked for -- it lifts only the force-push/deletion
  block, and touches nothing in `require-pr-review`.
- Note: the bypass list holds roles/Teams/Apps, not individual usernames. Promoting a
  future co-maintainer to Admin also hands them this same force-push bypass -- worth
  remembering before doing that.

Test both in **Evaluate** mode before flipping to **Active** (moot while private per
above, but worth doing once to confirm the configuration is correct).

## Required status checks (names from the `name:` of each job)

- `Python (lint + tests + coverage)` -- from `ci.yml`
- `.NET (build + tests + coverage)` -- from `ci.yml`
- `PowerShell (PSScriptAnalyzer)` -- from `ci.yml`
- `dotnet format` / `ruff format` -- from `format.yml`
- `Secret scan` / `Vulnerability scan` -- from `security.yml`

Turn these on as *required* only once a first run on each has gone green -- otherwise
PRs get blocked by checks that can never start.
