# Branch protection for `main`

## Why two rulesets, both in Active mode

This repo is public, on the GitHub Free plan. A ruleset's rules only *enforce* on
**public** repositories under Free -- a private repo can have one configured in the UI,
but it won't actually block anything, a documented GitHub limitation, not a bug in this
setup. Staying on Free was a deliberate choice over upgrading to Pro for enforcement
while the repo was still private.

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
- **Bypass list: the "Repository admin" role, bypass mode "pull request."** Originally
  configured with an empty bypass list, but GitHub categorically refuses to let anyone
  approve their own pull request, API included -- confirmed for real via `gh pr review
  --approve` on this project's own PR #1 (`GraphQL: Review Can not approve your own pull
  request`). With `* @ftrauernicht` as the only entry in `CODEOWNERS`, an empty bypass
  list would have meant no pull request on this repo could ever be merged by anyone.
  "pull request" bypass mode (not "always") keeps the actual protection: it only lifts
  the review-count/status-check requirements once a change is already inside a pull
  request -- direct pushes to `main` are still rejected outright for everyone, admin
  included (`gh pr merge --admin` is required even for the repo owner to merge past the
  missing approval; a plain `git push origin main` still gets `GH013: Repository rule
  violations`).

### Ruleset 2: `protect-branch-integrity`

- Target: `main`.
- Rules: **Block force pushes**, **Restrict deletions**.
- **Bypass list: the "Repository admin" role, bypass mode "Always."** This is the
  narrow, explicit override the owner asked for -- it lifts only the force-push/deletion
  block, and touches nothing in `require-pr-review`.
- Note: the bypass list holds roles/Teams/Apps, not individual usernames. Promoting a
  future co-maintainer to Admin also hands them this same force-push bypass -- worth
  remembering before doing that.

Both rulesets are created directly in **Active** mode via the API (`PUT/POST
repos/{owner}/{repo}/rulesets`) -- confirmed by reading each one back afterward:
`require-pr-review` reports `current_user_can_bypass: pull_requests_only` for the repo
owner, `protect-branch-integrity` reports `current_user_can_bypass: always`, matching
the bypass split above exactly.

## Required status checks (names from the `name:` of each job)

- `Python (lint + tests + coverage)` -- from `ci.yml`
- `.NET (build + tests + coverage)` -- from `ci.yml`
- `PowerShell (PSScriptAnalyzer)` -- from `ci.yml`
- `dotnet format` / `ruff format` -- from `format.yml`
- `Secret scan` / `Vulnerability scan` -- from `security.yml`

Turn these on as *required* only once a first run on each has gone green -- otherwise
PRs get blocked by checks that can never start.
