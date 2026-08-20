# Changelog

All notable changes to Whiskers are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow SemVer
(0.x = pre-1.0, minor bumps may contain breaking changes — noted explicitly).

## [Unreleased]

### Added
- **A server that stops answering now raises an alert.** A host dropping off the fleet was silent: the
  dashboard marked it unreachable, but nothing was sent — and every container alert and log-alert rule
  covering that host quietly stopped producing anything, which looks exactly like "all quiet". The health
  monitor now emits `server_unreachable` after two consecutive failed cycles
  (`HealthMonitor:ServerUnreachableCycles`) and `server_recovered` when the host is back. Both are
  rendered by every channel, link to `/servers`, and can drive an AI trigger. A server that has not
  answered once since startup gets a longer grace (`ServerUnreachableColdStartCycles`, default 10 cycles),
  because remote connections need a moment after a restart — without it a six-server fleet produced ten
  notifications per deploy.
- **The alert history is finally written.** `AlertHistory` has existed since the first migration but
  nothing ever wrote to it, so "what fired last week, and on which host?" was unanswerable. Every
  delivered notification is now recorded with its server, container, type and message; a
  `server_recovered` closes the outage rows it ends. Retention is the existing hourly prune.

### Fixed
- **Log alerts only ever watched one server.** The monitor's scan listed containers without a server id,
  which returns the **default** server's containers only — so every "on all containers" rule silently
  covered a single host while the other five in a six-server fleet were never read. The scan now uses the
  fleet-wide container list and fetches each container's logs from its own server. Pulled along with it:
  log watermarks and rule cooldowns are keyed by server + container id (container ids are unique per host
  only), hosts are scanned in parallel with a bounded per-fetch timeout, and the alert names the server and
  the line that matched.
- **A rule filtered to one container fired on all of them.** "No filter" was decided on `ContainerId`
  alone, but the UI dialog and the `create_log_alert` MCP tool only ever set `ContainerName` — so every
  container-specific rule degraded into an all-containers rule. Both fields are now considered. (Masked
  until now by the single-server scan; with a fleet-wide scan it would have meant an alert storm.)
- **An outage no longer swallows the log lines written during it.** An unreachable host returns an empty
  container list, which the monitors treated as "these containers are gone" and dropped their state for —
  re-baselining the host to "now" on recovery. Per-server state is now kept for hosts that did not answer,
  so the outage window is still scanned and a container that really stopped is still reported.
- **Log search searched one server too.** Without an explicit server it asked only the default host, while
  the page's container picker lists containers from every server — picking a remote container returned "no
  matches". Results now name the server they came from.
- **Muting a container silenced only a third of its alerts.** The per-container notification preferences
  were consulted by the health monitor alone; log alerts, CVE findings, image updates and metric alarms
  ignored them. The check now runs centrally for every producer.
- **Bounded log transfer.** With a `since` watermark the Docker call asked for *all* new lines, so one
  chatty container could pull its whole burst over a remote connection every minute, per cycle,
  fleet-wide. The tail limit (200 lines) now applies in both cases.
- The self-log guard that keeps Whiskers from alerting on its own log lines now applies only to the host
  Whiskers runs on. A container that merely shares its name on a remote host is a different process and
  stays monitored.

### Security
- Matrix messages are built from an HTML-encoded copy of the event. The log-alert detail now carries the
  matched log line — third-party text that must not reach an HTML body unescaped.

## [0.13.1] — 2026-07-24

### Fixed
- **The MCP server exposed no tools.** The module-driven MCP registration (added in 0.12.0) passed the
  enabled modules' tool types as a `Type[]`, which binds to the generic `WithTools<T>(T target)` overload
  instead of `WithTools(IEnumerable<Type>)` and registers **zero** tools. As a result the server
  advertised only the `logging` capability and every client got `-32601 "Method 'tools/list' is not
  available."` — the entire tool surface was invisible over MCP. Fixed by passing the tool types as
  `IEnumerable<Type>`. Added `McpToolRegistrationTests` to guard the overload binding, since MCP tool
  serving previously had no test coverage.

### Added
- The running app version is shown in the sidebar, under the Whiskers wordmark.

## [0.13.0] — 2026-07-17

The governance story, end to end: every agent action now carries one correlation id from guardrail
through approval to history, a guided setup takes you from zero to a governed agent in four steps,
and the whole UI is available in English.

### Added
- **Full English UI + in-app handbook.** Every page, notification and the handbook are localized;
  English is the default and the app follows the browser language (switch anytime). The AI chat
  now answers in the user's language.
- **Guided "Secure AI Operations" setup.** A new admin-only **AI Operations** page walks you to a
  governed agent in four steps: choose how AI connects, create a read-only MCP key (shown once),
  pick one of three starter guardrail presets (*Observe only* / *Safe operations* / *Approval
  required*) with a full policy preview before you activate it, then try it and verify. It reuses the
  existing key and guardrail flows — no new secret handling.
- **End-to-end correlated governance chain.** Every agent tool call carries a stable correlation id
  from guardrail → approval → execution → history and notification, so one action reads as one thread.
  Approvals now show the *real* required permission level, a derived risk band, the target
  server/workload and the guardrail preset + rule that matched — on the approval card and in the
  call-detail dialog.
- **Server groups & tags.** Give each server an optional group and free-form tags; the dashboard
  gains a group/tag filter and the `list_servers` MCP tool takes an optional `tag` filter — quick
  ways to narrow a larger fleet.
- **Keyless-signed release images (cosign / Sigstore).** Release images are signed with the release
  workflow's GitHub OIDC identity and logged in Rekor — verify with `cosign verify` (see README →
  Security → Supply chain). Complements the existing Trivy gate, SLSA provenance and SBOM.

### Changed
- **The governance surfaces explain themselves.** Agent History, Audit Log, Approvals and Guardrails
  now lead with what they are for; the Approvals empty state spells out that Confirm genuinely pauses
  execution, and Guardrails opens with the allow/confirm/block framing.
- Documentation restructured around the governance positioning, with screenshots and a demo script.

### Fixed
- **CI never actually ran the test suite.** `Whiskers.Tests` was missing from the solution file, so
  `dotnet test --no-build` exited 0 without executing a single test — every green CI run before this
  release was build + boot-gate only. The project is now in the solution and the Test step fails on a
  silently-empty run.

### Upgrading
- Adds one additive, nullable migration (the correlation id on the MCP call log) for both SQLite and
  PostgreSQL. It runs automatically on first start — no action required.

## [0.12.1] — 2026-07-11

Security hardening plus the features that landed right after the 0.12.0 tag was cut.
The three security items below were previously listed under 0.12.0, but the published
0.12.0 artifacts were built before they landed — they ship starting with this release.

### Security
- **SSH host keys are now verified** (trust-on-first-use, pinned in
  `<data>/ssh-keys/known_hosts`). *Behavior change:* an intentionally rebuilt server needs
  its line removed from that file before reconnecting.
- **Fail-closed authorization**: every endpoint/page requires authentication unless it
  explicitly opts out (login, setup wizard, health probes, HMAC webhooks, token-gated
  metrics). The SignalR hub is no longer reachable anonymously.
- MCP bearer scheme is parsed case-insensitively (RFC 7235).

### Added
- **Git-based deployments** (`gitdeploy` module) — clone/pull a repository on a target
  server and bring it up with Docker Compose; deploy tokens are vault-only (surfaced to git
  via a 0600 `GIT_ASKPASS` file), and the new `git-deploy` webhook action enables
  push-to-deploy from CI.
- **Container registries (v1)** — manage private registries in Settings with vault-stored
  credentials; image pulls authenticate automatically by registry-host match.
- **Localized navigation & app chrome** (English/German) — all nav items and groups.
- Audit log now also covers scheduler and webhook management actions in the UI.
- CI on every push/PR (build, full test suite, DI boot gate); first bUnit component tests.

### Changed
- Dependency refresh: MudBlazor 9.7, YamlDotNet 18, Npgsql EF provider 10.0.3,
  MCP SDK 1.4.1, NCrontab 3.4; release pipeline moved to docker/* actions v4/v7.

## [0.12.0] — 2026-07-11

First **published** release: the container image (`ghcr.io/lupusmalusdeviant/whiskers`)
and the Helm chart (`oci://ghcr.io/lupusmalusdeviant/charts/whiskers`) are built and
scanned by the release pipeline from this version on.

### Added
- **First-run setup wizard** — create the admin account in the browser; no `.env` needed.
  `VAULT_KEY` and the initial MCP key are generated/shown once in the wizard.
- **Local login** (ASP.NET Identity, email + password) alongside Google/OIDC; unattended
  admin seed via `WHISKERS_ADMIN_EMAIL` + `WHISKERS_ADMIN_PASSWORD_FILE`.
- **Self-backup & restore** of Whiskers' own data dir — optionally VAULT_KEY-encrypted
  (AES-256-GCM), crash-safe deferred-swap restore, schedulable backup task.
- **Kubernetes**: Helm chart for running Whiskers *on* a cluster (single-replica by design,
  restricted PodSecurity, PVC), and managing k3s/Kubernetes clusters *from* Whiskers —
  pods on the dashboard with owner grouping, logs, honest scale/rollout actions; kubeconfig
  stored encrypted in the vault. Least-privilege RBAC manifest in `deploy/k8s/`.
- **Module framework** — every feature area (CVE, agent, terminal, notifications, webhooks,
  cloud control, image updates, …) is a module, toggled via `Features:<id>:Enabled`.
- **PostgreSQL** as a second database provider next to SQLite (`WHISKERS_DB_PROVIDER`).
- **Release pipeline** — multi-arch image (amd64/arm64) gated by a Trivy CRITICAL scan,
  SBOM + provenance, GitHub Release with pinned compose files and `install.sh`.
- **Guided onboarding** — dashboard first-server guide, upfront Tailscale question,
  step-tracked onboarding with actionable errors and safe resume, production-readiness
  checklist in Settings.
- **Light mode** with dark/light/system toggle; auto-update **rollback** (snapshot-based);
  i18n groundwork (English default, German complete for migrated pages).

### Changed
- **Webhook secrets are mandatory** (HMAC `X-Hub-Signature-256` over the raw body).
  *Breaking:* pre-existing webhooks without a secret are disabled at boot (not deleted) —
  regenerate their secret in the UI and update the CI caller, then re-enable.
- Fresh installs no longer seed the "local" Docker server when no Docker socket is present
  (Kubernetes deployments start with an empty fleet).

### Fixed
- The webhook UI test button now sends a genuinely signed request.
- `DockerService` split into focused internals (no behavior change); numerous smaller fixes.

## [0.11.0] and earlier

Pre-publication development (dashboard, MCP server + acting agent with guardrails and
approvals, CVE scanning with dedup/age tracking, zero-SSH mesh+mTLS onboarding, cloud
control for Hetzner/Hostinger, terminal, deployments, notifications, audit log, …).
History: `git log v0.12.0` and `docs/reviews/`.
