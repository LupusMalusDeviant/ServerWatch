# outOfTheBox.md — Einfaches Installieren & Onboarding

> **Ziel:** Von „ich habe einen Linux-Server“ zu „Whiskers läuft, ich bin eingeloggt, mein erster Server ist verbunden“ in **unter 5 Minuten und ohne `.env`-Datei editieren zu müssen**. Zielgruppe: KMU-Admins und Selbsthoster ohne Whiskers-Vorwissen.
>
> **Messlatte:** Portainer = `docker run` + Admin-Passwort im Browser setzen. Coolify = `curl | bash` + Browser-Registrierung. Whiskers heute = git clone + `.env` aus 40 Variablen editieren + Google-OAuth-Credentials beschaffen. **Das ist der größte einzelne Adoption-Blocker.**

---

## 1. Ist-Zustand (verifiziert, damit klar ist, was wegfällt)

- Quick start laut README: `git clone` → `cp .env.example .env` → editieren → `docker compose up -d`.
- **Kein Setup-Wizard, kein First-Run-Flow.** Erste Anmeldung erfordert vorab konfiguriertes Google OAuth ODER OIDC ODER `AUTH_DISABLED=true`. Ohne Provider rendert die Login-Seite keinen funktionierenden Einstieg.
- `GOOGLE_ADMIN_EMAIL` seedet nur die Whitelist, **keine Admin-Rolle** → frische Instanz kann adminlos enden (`RoleService`, Default Viewer).
- `VAULT_KEY` fehlt in `.env.example` und Compose → Vault ist stillschweigend deaktiviert (nur Log-Warnung).
- Default-MCP-API-Key wird generiert und **ins Container-Log gedruckt**.
- Kein `HEALTHCHECK`, kein Probe-Endpoint → Installer/Nutzer kann nicht erkennen, wann die App bereit ist.
- Image wird nicht als fertiges Release gepullt, sondern lokal aus dem Klon gebaut (Dockerfile-Build beim ersten `up`).

## 2. Ziel-Erlebnis (das wird gebaut)

```bash
curl -fsSL https://get.whiskers.dev | bash        # oder manuell:
docker run -d -p 5100:8080 -v whiskers-data:/app/data \
  -v /var/run/docker.sock:/var/run/docker.sock \
  ghcr.io/lupusmalusdeviant/whiskers:latest
```
→ Browser öffnet `http://<host>:5100` → **Setup-Wizard** (Admin-Konto anlegen, Grundeinstellungen) → Dashboard mit lokalem Server bereits verbunden → geführtes „Ersten Remote-Server hinzufügen“ (bestehendes One-Click-Onboarding).

Drei Profile, klar benannt (README-Tabelle):
| Profil | Für wen | Wie |
|---|---|---|
| **Quick** | Ausprobieren, Single-Host | `docker run`-Einzeiler / Install-Script (neu) |
| **Standard** | KMU-Dauerbetrieb | `docker compose` aus Release-Assets, Reverse-Proxy-Beispiele (neu strukturiert) |
| **Hardened** | Security-Fokus | bestehendes `docker-compose.hardened.yml` (bleibt) |
| **Kubernetes** | Cluster | Helm (→ `kubernetesImplement.md`) |

---

## 3. Arbeitspaket W1 — Setup-Wizard (Kern des Dokuments)

**Konzept:** Ein First-Run-Zustand, der so lange aktiv ist, bis ein Admin existiert. In diesem Zustand leitet JEDER Request (außer statische Assets + `/healthz`) auf `/setup` um.

### Ablauf (Wizard-Schritte)
1. **Willkommen + Sprache** (de/en — setzt `missingFeatures.md` F2 voraus; bis dahin nur de mit en-Hinweis).
2. **Admin-Konto anlegen:** E-Mail + Passwort (lokales Identity-Konto aus `missingFeatures.md` F1) ODER „Ich nutze Google/OIDC“ → dann nur Admin-E-Mail eintragen (seedet Whitelist-Eintrag + **Admin-Rolleneintrag** in `roles.json` — behebt `changeme.md` C5).
3. **Sicherheits-Grundlagen (automatisch, nur Anzeige + Bestätigung):**
   - `VAULT_KEY`: wenn nicht als ENV gesetzt → Wizard generiert einen, zeigt ihn EINMALIG an („Jetzt sichern — ohne diesen Key sind gespeicherte Secrets nach Neuinstallation verloren“) und persistiert ihn in `/app/data/vault.key` (0600). Laden: ENV gewinnt über Datei (ENV bleibt der dokumentierte Weg für Profis; Datei ist der Zero-Config-Weg).
   - MCP-API-Key: statt Log-Ausgabe → im Wizard anzeigen (einmalig, Copy-Button) — behebt `changeme.md` C6. Option „MCP deaktiviert lassen“ (Default für KMU-Profil: aktiviert, aber nur Read-Level).
4. **Erster Server:** „Lokaler Docker-Host verbunden ✓“ (existiert schon als Default) + Button „Remote-Server hinzufügen“ → führt in den bestehenden `Servers.razor`-Onboarding-Dialog.
5. **Fertig:** Zusammenfassung + Links (Docs, Hardened-Profil-Hinweis, Backup einrichten).

### Implementierung (für Opus 4.8, konkret)
- **Zustandserkennung:** `ISetupStateService` (neu, `Services/Setup/`): `IsSetupComplete` = existiert mind. ein Admin (Identity-User mit Admin-Rolle ODER `roles.json`-Admin-Eintrag) — abgelegt als Flag-Datei `/app/data/setup-complete` (Cache) mit Re-Validierung beim Boot.
- **Middleware:** früh in der Pipeline (nach Static Files, vor Auth): wenn `!IsSetupComplete` und Pfad ∉ {`/setup`, `/healthz`, `/_blazor`, Assets} → Redirect `/setup`. Wenn `IsSetupComplete` und Pfad = `/setup` → Redirect `/`. **Damit ist die Wizard-Route selbst nie ein Sicherheitsloch: nach Abschluss ist sie tot.**
- **Race-Schutz:** Wizard-Abschluss ist atomar (eine Methode mit Lock im `ISetupStateService`); zweiter paralleler Abschlussversuch → Fehlermeldung. Wichtig, weil die Instanz vor dem ersten Admin unauthentifiziert erreichbar ist.
- **Erreichbarkeits-Fenster minimieren:** README-Hinweis, dass die Erstinstallation auf `127.0.0.1` bindet (heutiger Compose-Default `HOST_BIND=127.0.0.1` beibehalten!) bzw. das Install-Script den Wizard-Link mit Hinweis „vor Public-Exposure Setup abschließen“ ausgibt.
- **UI:** eigenes minimales Layout (kein NavMenu), MudBlazor-Stepper, Cat-Branding. Seite `Components/Pages/Setup.razor` + Unterkomponenten je Schritt (< 300 Zeilen pro Datei — Lehre aus `changeme.md` C4).
- ⚠️ **Auth-Off-Limits-Regel:** Schritt 2 berührt Auth-Konfiguration → diesen Teil als eigenen, klar abgegrenzten PR mit User-Review; Middleware-Reihenfolge NICHT verändern (NIED-25.2-Entscheid respektieren), die Setup-Middleware kommt VOR den bestehenden Auth-Block, ändert ihn aber nicht.

**Abhängigkeiten:** F1 (lokaler Login) für Schritt 2-Variante A; ohne F1 kann der Wizard mit Variante B (OIDC/Google-E-Mail + `AUTH_DISABLED`-Warnpfad) starten. Empfehlung: F1 zuerst, Wizard direkt danach.

---

## 4. Arbeitspaket W2 — Veröffentlichtes Image + Install-Script

Heute wird das Image beim Nutzer gebaut. Ändern:

1. **GitHub Actions Release-Pipeline** (`.github/workflows/release.yml`, neu):
   - Trigger: Tag `v*`. Build multi-arch (`linux/amd64`, `linux/arm64` — KMU-NAS/RPi!), Push nach `ghcr.io/lupusmalusdeviant/whiskers` mit Tags `latest`, `X.Y.Z`, `X.Y`.
   - Trivy-Scan des eigenen Images als Gate (das Tool, das CVEs scannt, darf nicht mit Criticals shippen — Marketing-Argument).
   - SBOM (`syft`) + Provenance-Attestation anhängen; Release-Assets: `docker-compose.yml`, `docker-compose.hardened.yml`, `install.sh`, Checksums.
2. **`install.sh`** (Repo-Root `deploy/install.sh`, vom Release verlinkt):
   - Prüft: Docker vorhanden (sonst Anleitung/Abbruch — NICHT ungefragt `get.docker.com` ausführen; das macht nur das Server-Onboarding mit explizitem Opt-in), Ports frei, arch supported.
   - Fragt: Port (Default 5100), Bind (Default 127.0.0.1, Hinweis auf Reverse Proxy), Datenpfad (Default Named Volume).
   - Schreibt minimale `docker-compose.yml` nach `./whiskers/`, `docker compose up -d`, wartet auf `/healthz` (→ `changeme.md` C11 ist Voraussetzung), gibt Wizard-URL aus.
   - Idempotent: erneuter Lauf = Update (`pull` + `up -d`).
3. **`.env.example` entschlacken:** Nach W1 sind nur noch OPTIONALE Variablen nötig → Datei in Sektionen mit „Alles optional — Grundbetrieb braucht KEINE .env“ umbauen. `VAULT_KEY` dokumentieren (auch wenn Wizard ihn erzeugen kann).
4. **README-Quickstart neu schreiben:** Einzeiler zuerst, dann Standard-Compose, dann Hardened, dann Helm-Verweis. Der heutige „git clone“-Weg wandert in den Development-Abschnitt.

**Abhängigkeiten:** C11 (Healthz) für den Wait-Step; W1 damit „ohne .env“ stimmt. Multi-arch-Build erfordert Prüfung der apt-Pakete im Dockerfile (tailscale/sshpass auf arm64 verfügbar — verifizieren!).

---

## 5. Arbeitspaket W3 — Geführtes In-App-Onboarding (nach dem Wizard)

Das starke, bereits existierende Server-Onboarding (`OnboardingService`: SSH-Bootstrap → Tailscale → Docker → mTLS → SSH-Credentials löschen) sichtbarer und robuster machen:

1. **Empty-State des Dashboards:** Wenn nur „local“ verbunden → Karte „Fügen Sie Ihren ersten Server hinzu“ mit 2 Wegen: (a) One-Click-Onboarding (empfohlen, Erklärgrafik der 3 Planes aus `docs/ARCHITECTURE.md`), (b) „Nur Docker-API/SSH verbinden“ (klassisch, ohne Mesh — für Nutzer, die kein Tailscale wollen).
2. **Tailscale-Hürde explizit machen:** Onboarding setzt ein Tailscale/NetBird-Konto voraus — im Dialog VOR Schritt 1 abfragen („Haben Sie ein Tailscale-Konto? → Login-Link kommt gleich / Nein → klassischer Modus“). Heute erfährt der Nutzer das erst mitten im Flow (`::TS_LOGIN::`-Marker).
3. **Fehlertoleranz:** `OnboardingService` (337 Zeilen, 0 Tests — `changeme.md` C17) bekommt: Schritt-Status-Objekt (welcher der ~8 Schritte lief durch), Resume nach Abbruch (idempotente Schritte), verständliche Fehlertexte je Schritt (statt roher stderr). Command-Building-Tests ergänzen.
4. **Checkliste „Produktionsreif?“** in Settings (statisch, kein neues Framework): Auth konfiguriert ✓/✗, Vault aktiv, Backup eingerichtet, Hardened-Profil, HTTPS am Proxy, Update-Policy gesetzt. Jeder Punkt verlinkt die passende Settings-Sektion. Billig zu bauen, enormer „das Ding fühlt sich fertig an“-Effekt.

> 🟢 **W3 erledigt** (Commit `425af6a`, 2026-07-11): (1) Dashboard-Empty-State-Karte solange nur „local" verbunden (One-Click-Onboarding empfohlen vs. „Nur Docker-API/SSH", Deep-Links `servers?add=onboard|classic` öffnen den Dialog vorbelegt; nur für Admins). (2) Tailscale-Frage als Vorab-Dialog VOR Speichern/Onboarding (Abbruch lässt den Dialog offen → klassisch speichern). (3) `OnboardingService` → schrittgetracktes `OnboardingResult` (Enum + Klartext-Hinweis je Schritt + Retry-Button; Resume = idempotenter Re-Run, bereits verbundene Tailscale-Nodes werden erkannt), Command-Builder nach `OnboardingCommands` extrahiert (**19 Command-Building-Tests**: Slug-Allow-List, base64-Transport, Injection-Versuche) + Tailnet-IP-Validierung via `IPAddress.TryParse` (Härtung ggü. Spec). (4) „Produktionsreif?"-Panel in Settings (`IProductionReadinessService`: Auth, VAULT_KEY, SelfBackup-Task, Update-Policy, non-root, HTTPS — HTTPS-Flag aus `NavigationManager`, da `IHttpContextAccessor` im Circuit unzuverlässig).

## 6. Arbeitspaket W4 — Demo-Modus (optional, P2)
`WHISKERS_DEMO=true`: seedet Fake-Server + Fake-Container + Beispiel-CVEs (In-Memory-`IDockerService`-Fake), alle schreibenden Ops geblockt. Ermöglicht öffentliche Demo-Instanz + Screenshots für die Website (`project_serverwatch_website`). Erst nach dem `IDockerService`-Seam-Refactor sinnvoll (Fake-Implementation wird dann trivial) → nach `kubernetesImplement.md` Track B Schritt 1.

---

## 7. Wie NICHT

- **KEIN** Default-Passwort („admin/admin“) — Wizard erzwingt Passwort-Anlage; unbeaufsichtigte Installs setzen `WHISKERS_ADMIN_EMAIL`/`WHISKERS_ADMIN_PASSWORD_FILE` als ENV-Alternative (Secret-File, nicht Plain-ENV, dokumentieren).
- **NICHT** `AUTH_DISABLED=true` als Quickstart-Empfehlung dokumentieren — der Wizard macht es überflüssig; die Option bleibt als LAN-Escape-Hatch.
- **KEINE** stillen Netzwerk-Defaults ändern (Bind bleibt 127.0.0.1) — „einfach“ darf nicht „öffentlich exponiert“ heißen.
- **NICHT** das Install-Script Docker ungefragt installieren lassen (Vertrauen! Nur mit `--install-docker`-Flag).
- **KEINEN** eigenen Update-Mechanismus für Whiskers selbst im Installer verstecken — Updates laufen über `docker compose pull` bzw. die bestehende AutoUpdate-Policy, dokumentiert.

## 8. Abhängigkeiten & Reihenfolge

```
C11 (healthz) ──┐
F1 (lokale Auth)┼─→ W1 Wizard ─→ W2 Release+Installer ─→ W3 geführtes Onboarding ─→ W4 Demo
C5/C6 (Fixes) ──┘        (F2 i18n parallel; Wizard-Strings von Anfang an über IStringLocalizer)
```

## 9. Definition of Done
- [ ] Frische VM, ein `docker run`-Befehl (oder `install.sh`), KEINE `.env` → Wizard → Login → Dashboard: **< 5 Minuten**, gestoppt und im README als geprüfter Wert dokumentiert.
- [ ] Kein Secret erscheint jemals in `docker logs`.
- [ ] Abbruch mitten im Wizard/Onboarding hinterlässt keinen kaputten Zustand (Resume möglich).
- [ ] `ghcr.io`-Image multi-arch, Trivy-clean (keine Criticals), mit SBOM.
- [ ] README-Quickstart entspricht exakt dem realen Ablauf (von einer unbeteiligten Person nachgestellt).
