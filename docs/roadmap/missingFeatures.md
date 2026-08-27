# missingFeatures.md — Feature-Lücken vs. Portainer, Coolify & Co.

> **Ziel:** Was fehlt Whiskers (v0.11.0), um Portainer/Coolify/Dockge/Komodo nicht nur zu erreichen, sondern zu deklassieren — priorisiert, mit Implementierungsskizze pro Feature.
>
> **Strategische Einordnung zuerst:** Whiskers gewinnt NICHT, indem es Portainer Feature für Feature nachbaut. Die Differenzierer sind bereits da und haben bei keinem Wettbewerber ein Äquivalent:
> - **MCP-Server + handelnder AI-Agent mit Guardrails/Approvals** (Alleinstellungsmerkmal)
> - **SSH-key-freier Betrieb** (Mesh + mTLS + step-ca)
> - **Integriertes CVE-Scanning** (Trivy + OS) mit Dedup/Age-Tracking
> - **Out-of-band Cloud-Control** (Hetzner/Hostinger, provider-agnostisch)
>
> Die Lücken unten sind das, was Adoption VERHINDERT (P0), was im Feature-Vergleich schmerzt (P1), und was „rund“ macht (P2).

Verifizierter Ist-Stand pro Feature: siehe Inventar-Tabelle am Ende.

---

## P0 — Adoption-Blocker (ohne diese probiert es niemand ernsthaft aus)

### F1 — Lokale Authentifizierung + 2FA/Passkeys
**Lücke:** Es gibt NUR Google OAuth und generisches OIDC (kein ASP.NET Identity, kein lokaler User-Store). Ein KMU ohne IdP kann sich nur mit `AUTH_DISABLED=true` behelfen. Portainer hat lokale User out of the box.
**Implementierung:**
- ASP.NET Core **Identity** mit EF-Store einführen (nutzt den `MetricsDbContext` NICHT — eigener `IdentityDbContext` gegen dieselbe DB, damit `stableDB.md`-Provider-Switch greift). Cookie-Schema bleibt das bestehende; Identity nur als zusätzlicher Login-Provider neben Google/OIDC.
- Login-Seite: lokales Formular + „Login mit Google/OIDC“-Buttons (nur anzeigen, was konfiguriert ist — heutiger Zustand „leere Login-Seite ohne Provider“ verschwindet).
- 2FA: TOTP über Identity-Bordmittel; Passkeys (WebAuthn) via `Fido2NetLib` als zweite Ausbaustufe (eigener PR).
- Rollen-Mapping: Identity-User bekommen `roles.json`-Einträge wie OIDC-User (per E-Mail-Key) — KEIN paralleles Rollensystem bauen.
- ⚠️ Auth-Bereich ist Off-Limits-Zone → User-Freigabe einholen; mit `changeme.md` C5 (Admin-Bootstrap, fail-closed Whitelist) und dem Setup-Wizard (`outOfTheBox.md` §3) als EIN Auth-Arbeitsblock planen.
**Abhängigkeiten:** stableDB.md (Identity-Tabellen sollen direkt in beide Provider), outOfTheBox.md Wizard.

### F2 — Englische UI / i18n
**Lücke:** Alle UI-Strings hartkodiert Deutsch (kein `IStringLocalizer`, keine `.resx`). Internationale Nutzer sind komplett ausgeschlossen — der härteste Reichweiten-Blocker.
**Implementierung:**
- `AddLocalization()` + `IStringLocalizer<T>` mit `.resx` (`Resources/`-Ordner, `de` + `en`; **`en` als Default/Fallback**, `de` vollständig — dreht die heutige Situation um, README verspricht bereits Englisch als Ziel).
- Mechanischer Sweep: alle 35 Pages + Shared-Komponenten + `NavMenu.razor` + Snackbar-Texte (179 Aufrufe) + `HelpContentService.cs` (490 Zeilen hartkodiertes Handbuch → in Markdown-Ressourcen je Sprache auslagern).
- Sprachwahl: `RequestLocalization` (Accept-Language) + User-Override in Settings (persistiert in `app-settings.json`).
- **Vorgehen für ein schwächeres Modell:** Seite für Seite, je PR max. 3 Seiten; Skript-artige Ersetzung (String → Resource-Key `Page_Element_Zweck`); danach visuelle Stichprobe. NICHT Notification-/Alert-Texte vergessen (`Services/Notifications`, `LogMonitor`).
**Abhängigkeiten:** keine. Kann sofort parallel laufen. Vor F2 keine Marketing-/Show-HN-Aktionen.
> 🟢 **F2-Start erledigt** (Branch `feat/f2-i18n-start`, 2026-07-09): Infrastruktur (`AddLocalization` + `RequestLocalization`, **en = Default/Fallback**, `de` = volle Übersetzung; Kultur aus Cookie → Accept-Language), `SharedResource`-Tabelle (neutrale `SharedResource.resx` EN + `de`-Satellite), anonymer `/set-culture`-Endpoint, `LanguageSwitcher` in der App-Bar, **Login** als Pilot lokalisiert (Key-Konvention `Page_Element_Purpose`). E2E verifiziert: `/login` EN default, DE via `Accept-Language`/Cookie; `/set-culture` setzt das Kultur-Cookie; build + 298 Tests + Boot-Gate grün. **Additiv — kein Auth-Middleware-Reorder.** **Rest = Daueraufgabe** (seitenweise: ~34 Seiten + 179 Snackbars + Notification-/Log-Texte + `HelpContentService`); `NavMenu` bleibt für SAP Phase 1 (dort auf `IModuleRegistry`/`NavItem.LocKey` umgestellt). Doku: `src/Whiskers/Resources/README.md`. **Noch keine „English supported"-Marketing-Aussage** (nur Login ist EN).

### F3 — Backup & Restore von Whiskers selbst
**Lücke:** `VolumeBackups` sichert Docker-Volumes, `backup_database` fremde DBs — aber Whiskers' eigener State (`/app/data`: 12 JSON-Stores, metrics.db, Keys, Certs) hat KEINE In-App-Sicherung; `ConfigExport` wurde im Review gelöscht (NIED-23). Ein Tool, das Server verwaltet, aber sich selbst nicht sichern kann, fällt im ersten Vergleichstest durch.
**Implementierung:**
- `IBackupService.CreateBackupAsync()`: konsistenter Snapshot von `/app/data` → tar.gz. SQLite via `VACUUM INTO` (Online-Backup, kein File-Copy bei laufendem WAL!); JSON-Stores über deren Lock lesen; `vault.json` bleibt verschlüsselt (Master-Key ist ENV, gehört NICHT ins Backup — dokumentieren!).
- UI: Settings → „Backup & Restore“: manueller Download, optionaler Zeitplan (bestehenden `SchedulerService` nutzen, Ziel: lokales Verzeichnis/Volume), Restore per Upload mit expliziter Bestätigung + automatischem Pre-Restore-Backup (DB-Safety-Regel des Projekts!).
- Restore-Ablauf: Upload → Validierung (Manifest mit Version) → App in Wartungsmodus (neue Middleware, 503 + Banner) → Dateien ersetzen → Prozess-Neustart anstoßen (Container-Restart-Policy übernimmt).
- Version im Manifest gegen `InitialCreate`-Migrationsstand prüfen; Restore älterer Backups triggert normalen Migrationslauf.
**Abhängigkeiten:** C1 (DataPaths). Nach `stableDB.md` um `pg_dump`-Pfad erweitern.
> 🟢 **F3 erledigt** (Commit `9b10bba`, 2026-07-10): `IBackupService`/`BackupService` — In-Process tar.gz von `/app/data` (`System.Formats.Tar`, KEIN Helper-Container wie VolumeBackupService), SQLite via `VACUUM INTO` (gated `IsSqlite()`; beide DbContexts teilen eine metrics.db → ein VACUUM erfasst beide), `backups/`+WAL-Sidecars+`*.tmp` ausgeschlossen. **Erweiterung ggü. Spec (User-Entscheidung):** herunterladbares Archiv wird **VAULT_KEY-abgeleitet AES-256-GCM verschlüsselt** (gerahmt/streamend, Tamper+Truncation-authentifiziert; Klartext-Fallback + UI-Warnung wenn `VAULT_KEY` unset) — weil das Archiv Klartext-Secrets (ssh-keys/mtls/keys/) enthält; Restore braucht denselben `VAULT_KEY`. **Restore = crash-safe „deferred swap on boot"** statt Live-Datei-Ersatz: validieren (Krypto/Manifest/Provider/Schema) → Pre-Restore-Backup → Staging → `.restore-pending`-Marker → `StopApplication()`; `RestoreBootHandler` tauscht beim Neustart in `Program.cs` VOR DB/Keys-Open (immutable Staging, idempotent, fail-closed, `-wal`-Löschung, Symlink-Ablehnung, Zip-Slip-Guard). Manifest-**Migrations-Subset-Check** (älter/gleich OK, neuer abgelehnt) wie gefordert. **Wartungsmodus** (`IMaintenanceStateService` + Middleware am W1-Seam, 503 + `/readyz`-Drain, `/healthz` unberührt). Zeitplan via neuem `ScheduledTaskType.SelfBackup` (ans Enum-Ende → INT-safe) + Retention. **KEINE DB-Migration** (Klartext-Sidecar-Manifeste statt DB-Tabelle). build + 467/469 Tests (nur Docker-`PostgresSmokeTests` fehlen). **Offen:** `pg_dump`-Pfad für Postgres (Follow-up nach stableDB; F3 warnt auf PG, dass die relationale DB nicht im tar ist); dedizierter Multipart-Upload für sehr große Restores. **✅ Live auf Badwolf** (verifiziert 2026-07-16: `9b10bba` ist Vorfahre des laufenden `c9e5bb0`, Image vom 2026-07-13 — F3 ist mit einem späteren Deploy mitgefahren, ein F3-eigener Deploy war nie nötig).

### F4 — Kubernetes (eigenes Dokument)
Siehe `kubernetesImplement.md` — sowohl „Whiskers AUF K8s“ (Helm) als auch „K8s MIT Whiskers verwalten“ (k3s-Fokus laut Roadmap). Für KMU-Adoption ist Track A (Helm-Deployment) P0, Track B (k3s verwalten) P1.

---

## P1 — Vergleichstest-Lücken (hier verliert Whiskers heute gegen Portainer/Coolify)

### F5 — Git-basierte Deployments (Coolify-Kernfeature)
**Lücke:** Deploy ist pull-image-only; kein „Repo-URL rein → Build → Run“. Genau das ist Coolifys Existenzgrund.
**Implementierung (bewusst schlank, kein Buildpack-Klon):**
- Neuer `Services/GitDeploy/`-Bereich: `IGitDeployService` — klont Repo (https + Token/Deploy-Key aus Vault) auf dem ZIELSERVER via `IHostCommandExecutor`, baut mit `docker build` bzw. `docker compose build` (Compose-Datei im Repo = bevorzugter Pfad), tagged `whiskers-build/<app>:<gitsha>`, deployed über den bestehenden `IDeploymentService`.
- Webhook-Trigger: bestehende `WebhookService`-Infrastruktur nutzen (HOCH-12 Teil 2 = Secret-Pflicht VORHER umsetzen) → Push-Event = Rebuild+Redeploy.
- UI: neuer Menüpunkt „Git Deploy“ unter Deployment; Felder Repo/Branch/Compose-Pfad/Env; Build-Log live streamen (SignalR, Muster von `Terminal.razor` übernehmen).
- **Nicht bauen:** Nixpacks/Buildpacks-Autodetektion, PR-Preview-Environments — Post-1.0.
**Abhängigkeiten:** HOCH-12 Teil 2; Vault für Deploy-Keys.
> 🟢 **F5 erledigt** (Commit `2e4d809`, 2026-07-11): `Services/GitDeploy/` + `gitdeploy`-Modul + `/git-deploy`-Seite. Klon/Build/Up auf dem ZIELSERVER via `IHostCommandExecutor`; Tokens NUR im Vault (`git-token:{id}`), auf dem Target als 0600-GIT_ASKPASS-Datei (base64-Transport, nie in argv/Logs); Webhook-Aktion `git-deploy` (HMAC-Pflicht via F11) über Core-Contract + Noop; 7 Command-Building-Tests. **Abweichungen:** Build-Log via Blazor-Progress-Dialog statt SignalR-Stream (Onboarding-Muster, ausreichend); nur https-Remotes (kein SSH-Key-Verteilen in v1).

### F6 — Docker-Events-Stream statt Poll-only
**Lücke:** Kein `MonitorEventsAsync`; Zustandsänderungen werden nur im Poll-Intervall bemerkt. Portainer zeigt Events live.
**Implementierung:** Neuer HostedService `DockerEventMonitor` je verbundenem Server (über `IDockerConnectionManager`), abonniert `system/events` (Docker.DotNet `MonitorEventsAsync`), pusht in `ContainerHub` (SignalR) → Dashboard-Badges live; Events zusätzlich als Ringpuffer (in-memory, 1000 Events) für eine „Events“-Ansicht. Reconnect-Logik an den Self-Healing-Mechanismus des ConnectionManagers hängen. Health/Stop/OOM-Notifications können mittelfristig vom Poll- auf Event-Trigger wechseln (separater PR, Poll als Fallback behalten).

### F7 — Ressourcen-Limits editieren
**Lücke:** Memory/CPU-Limits werden nur angezeigt; `ContainerUpdateParameters` wird nirgends genutzt.
**Implementierung:** `IDockerService.UpdateContainerResourcesAsync(serverId, containerId, ResourceLimits)` → `Containers.UpdateContainerAsync`. UI in `ContainerDetail` (nach C4-Split in den Overview-Tab). MCP-Tool `update_container_resources` (Level: write). Achtung Socket-Proxy-Verb-Whitelist: `POST /containers/{id}/update` muss in der hardened-Proxy-Config erlaubt werden (`docker-compose.hardened.yml` + `deploy/telemetry`-Templates prüfen).

### F8 — Registry-Verwaltung in der UI
**Lücke:** Harbor/GHCR-Credentials nur per ENV; kein UI-CRUD, keine privaten Registries pro Deploy.
**Implementierung:** `RegistryConfig`-Store (nach `stableDB.md` als DB-Tabelle, Credentials im Vault referenziert), Settings-Panel „Registries“, `ImageSearchProvider`-Instanzen dynamisch aus dem Store speisen (statt fix aus Options), `PullImageAsync` bekommt `AuthConfig` aus der passenden Registry. 
**Abhängigkeiten:** Vault aktiviert (VAULT_KEY-Onboarding aus `outOfTheBox.md`).
> 🟡 **F8 v1 erledigt** (Commit `6e70b38`, 2026-07-11): `RegistryConfig`-Store (`registries.json`) + Credentials im Vault (`registry-cred:{id}`), Settings-Panel „Registries" (auditiertes CRUD), **authentifizierte Pulls** — `PullImageAsync` matcht den Registry-Host der Image-Referenz (Docker-Konventionen, 9 Tests) und übergibt `AuthConfig`. **Offen (dokumentierter Follow-up):** Such-Provider dynamisch aus dem Store speisen (Suche liest weiter `ImageSearch`-Settings).

### F9 — Server-Gruppen & Tags (eigene Roadmap, README)
**Implementierung:** `ServerConfig` + `Tags: List<string>` + `Group: string?`; Dashboard/Nav-Filter; MCP-Tools bekommen optionalen `tag`-Filter. Klein, aber hoher Alltagsnutzen. Keine Abhängigkeiten.

### F10 — OpenAPI/Dokumentierte HTTP-API
**Lücke:** Kein Swagger; die einzige „API“ ist MCP + Webhooks. Integratoren erwarten REST.
**Implementierung (pragmatisch):** KEINE parallele REST-API bauen. Stattdessen: (a) MCP-Tool-Katalog als generierte Doku-Seite (aus `[Description]`-Attributen + `DefaultToolLevels` — Generator existiert quasi in `AgentToolRegistry`), öffentlich in `docs/` + im Hilfe-Bereich; (b) `Mcp`-Endpoint sauber dokumentieren (Auth, JSON-RPC-Beispiele, curl). Das positioniert MCP als DIE API (Differenzierer!) statt REST-Nachbau. Erst bei echtem Bedarf (Integrations-Requests) eine dünne REST-Fassade über dieselben Services.

### F11 — Webhook-Secret-Management (HOCH-12 Teil 2)
Siehe `changeme.md` A-Block — hier nur als Feature-Sicht: Pflicht-Secret, One-Time-Anzeige, HMAC-Signatur-Validierung (`X-Hub-Signature-256`-kompatibel → GitHub/Gitea/GitLab-Webhooks funktionieren nativ). Voraussetzung für F5.
> 🟢 **F11 erledigt** (Commit `cf4f556`, 2026-07-11): Secret-Pflicht serverseitig (256-bit, `WebhookService` = einziger Generator; Entity-Default leer), `TriggerAsync` fail-closed (kein Secret → abgelehnt; Signatur über Raw-Body immer Pflicht, constant-time), One-Time-Anzeige bei Create + Regenerate (Copy-Dialog), Enable-Guard für secret-lose Alt-Webhooks, **signierter UI-Test** (`TriggerSignedTestAsync` — der alte Test-Button sendete unsigniert), Boot-Upgrade via `WebhooksModule.InitializeAsync` deaktiviert secret-lose Alt-Webhooks (nicht löschen) + `webhook_disabled`-Notification. **Nebenbefund behoben:** `IWhiskersModule.InitializeAsync` war seit Phase 0 nie verdrahtet → läuft jetzt in `RunWhiskersStartupAsync` NACH der DB-Migration. 13 Tests (`WebhookSecretTests`).

### F12 — Light Mode + Theme-Toggle
**Lücke:** `MainLayout.razor:4` hartkodiert `IsDarkMode="true"`; `AppThemes.cs` hat nur PaletteDark.
**Implementierung:** `PaletteLight` definieren, `MudThemeProvider @bind-IsDarkMode` + System-Preference-Erkennung (`prefers-color-scheme` via JS-Interop, MudBlazor `ObserveSystemThemeChange`), Persistenz in User-Settings. Klein (1 PR). Cat-Branding-Farben beibehalten.
> 🟢 **F12 erledigt** (Commit `27d2a11`, 2026-07-11): volle `PaletteLight` (neutrale Zink-Flächen, Akzente bleiben Theme-getrieben), `html[data-mode="light"]`-CSS-Variablenblock (hartkodierte Dark-Werte in app.css auf Variablen umgestellt), Hell/Dunkel/System im AppBar-Palette-Menü (lokalisiert en/de), System folgt dem OS live via `MudThemeProvider.WatchSystemDarkModeAsync` (MudBlazor 9: so heißt die API, nicht `ObserveSystemThemeChange`), Pre-Paint-Script gegen Flash, Persistenz `sw-mode` in localStorage (konsistent zum bestehenden `sw-theme`-Muster statt Server-Settings — Abweichung, bewusst).

---

## P2 — Abrunden (nach 1.0 ok, aber notieren)

| Feature | Kurzskizze |
|---|---|
| **Teams / feingranulare RBAC** | Heute 3 globale Rollen. Ausbaustufe: per-Server-Scoping (`RoleEntry` + optionale `ServerIds`), erst wenn echte Multi-User-Nachfrage da ist. Multi-Tenancy NICHT anstreben (Positionierung: Single-Team-Tool). |
| **Volume-Datei-Browser** | Über One-Shot-Helper-Container (`alpine` + Volume-Mount, `ls`/`cat` über Exec) — Muster von `RunHostShellAsync` wiederverwenden. Security-Review nötig (Pfad-Traversal). |
| **E-Mail-Einladungen** | Erst nach F1 (Identity) sinnvoll: Invite-Token → Registrierungslink. `EmailNotificationService` als Versandkanal existiert. |
| **PWA/Mobile-Polish** | MudBlazor-Grid ist responsive; PWA-Manifest + Icon reicht für „installierbar“. |
| **LDAP/SAML** | Nicht bauen. OIDC deckt moderne IdPs; LDAP-Nachfrage an Keycloak/Authentik als Bridge verweisen (dokumentieren in README/FAQ). |
| **Traffic-/Anomalie-Trigger** (README-Roadmap) | Eigener Plan nach 1.0; heutiger Rolling-Z-Score bleibt bis dahin. |
| **Approval-Gate für externe MCP-Calls** (README-Roadmap) | Architektur-relevant: Approval-Hold in die MCP-Pipeline (Middleware vor Tool-Dispatch) statt nur im Agent-Runtime. Mit `RoadToSAP.md`-Modularisierung des MCP-Hosts zusammen planen. |
| **Distroless-Image** | Bereits in Arbeit laut Memory/Container-Distribution — Sequenzierung dort. |

---

## Verifiziertes Feature-Inventar (Ist-Stand, Kurzreferenz)

| Feature | Status |
|---|---|
| Compose-Editor UI / Deploy | ✅ (`ComposeEditor.razor`, `Deploy.razor`) — kein Raw-YAML-Stack-Manager |
| App-Templates / One-Click | ✅ (`AppStore.razor`, `TemplateService`) |
| Container exec/attach UI | ✅ (xterm, Host + Container) |
| Audit-Log | ✅ |
| CI/CD-Webhooks | ⚠️ vorhanden, secret-lose Trigger offen (F11) |
| Registry-Suche | ⚠️ Suche ja (Hub/GHCR/Harbor), Verwaltung nur ENV (F8) |
| OIDC/SSO | ⚠️ Google + OIDC, kein lokaler Login (F1), kein LDAP (bewusst) |
| RBAC | ⚠️ 3 globale Rollen, kein Scoping |
| Git-Build-Deploy | ❌ (F5) |
| Events-Stream | ❌ (F6) |
| Resource-Limits editieren | ❌ (F7) |
| Whiskers-Self-Backup | ❌ (F3) |
| OpenAPI/REST | ❌ (F10 — bewusst MCP-first) |
| i18n / Englisch | ❌ (F2) |
| Light Mode | ❌ (F12) |
| 2FA/Passkeys nativ | ❌ (F1, heute an IdP delegiert) |
| Kubernetes | ❌ (F4 → eigenes Dokument) |
| Multi-Tenancy | ❌ (bewusst nicht) |

## Priorisierung in einem Satz
**Erst F1+F2 (Zugang & Sprache), F3 (Vertrauen), F4/Track A (K8s-Deploy), dann F5–F12 in Vergleichstest-Reihenfolge — und die Differenzierer (MCP/Agent/CVE/Zero-SSH) in jedem Release sichtbar weiter schärfen statt Feature-Parität-Vollständigkeit zu jagen.**
