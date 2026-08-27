# beatPortainerCoolify.md — Angriffsplan (Stand v0.12.1, 2026-07-11)

> **Strategische Prämisse:** Wir gewinnen NICHT über Feature-Parität (Portainer: 10 Jahre
> Vorsprung + Firma; Coolify: riesige Community + PaaS-Fokus). Wir gewinnen über die unbesetzte
> Position **„das sicherste Self-Hosted-Tool, das ein AI-Agent bedienen darf"** — plus einen
> Kern, der gut genug ist, dass niemand Portainer *daneben* braucht. „Boden aufwischen" heißt:
> in dieser Nische konkurrenzlos sein und im direkten Vergleichstest keinen K.-o.-Punkt mehr
> hergeben.
>
> Ist-Stand als Referenz: `missingFeatures.md` (F1–F12 größtenteils ✅), `roadTo1_0.md`.
> Realitäts-Abgleich Website/Doku: 2026-07-11 erledigt (Seiten + In-App-Handbuch auf 0.12.1).

---

## Phase 0 — Launch-Reife (der eine erste Eindruck; ~2–4 Wochen)

Kein Marketing vor Abschluss dieser Phase. Reihenfolge = Priorität.

### L1 — i18n fertig: 100 % Englisch (Blocker Nr. 1)
- **Rest-Sweep** der ~15 verbliebenen Seiten + ~179 Snackbar-/Notification-Texte (Muster steht:
  `Nav_*`/`Page_Element_Purpose`-Keys, seitenweise Batches, EN default).
- **In-App-Handbuch ist DEUTSCH-only** (`HelpContentService`, 0 Localizer-Treffer) — für
  internationale Nutzer der peinlichste Fund. Lösung pragmatisch: Kapitel-Struktur behalten,
  Inhalte in `HelpContentService.en.cs`-Pendant (oder Markdown-Ressourcen je Sprache, wie in
  missingFeatures F2 skizziert). EN zuerst vollständig, DE bleibt.
- Definition of Done: App-Durchlauf mit `Accept-Language: en` ohne ein einziges deutsches Wort.

### L2 — Screenshots & Doku-Politur
- Screenshots auf der Website sind **pre-Rebrand** (28.06., zeigen alte UI). Neu aufnehmen:
  Dashboard, Agent-Widget, Guardrails, Freigabe-Karte, CVE-Monitor, K8s-Pods, Setup-Wizard —
  je hell UND dunkel, EN-UI (internationale Zielgruppe). Lokal via `Auth:Disabled` + Seed-Daten.
- Landing: 30–60-Sekunden-GIF/WebM „Claude repariert einen ungesunden Container — mit Freigabe"
  (das EINE Asset, das die Positionierung zeigt).
- `/help/*.png`-Platzhalter der In-App-Hilfe mit echten Captures füllen (HelpFigure ist da).

### L3 — 2FA (TOTP) für den lokalen Login
- ASP.NET Identity bringt TOTP mit; UI: Setup-QR + Recovery-Codes in den Profil-Einstellungen.
- Kleinster Baustein mit größtem Vertrauens-Hebel („lokaler Login ohne 2FA" ist das erste,
  was ein r/selfhosted-Kommentar zerreißt). Passkeys = Ausbaustufe danach, nicht Blocker.
- ⚠️ Auth = Off-Limits-Zone → User-Go einholen, als eigener PR.

### L4 — Vertrauens-Infrastruktur (fast alles schon da — sichtbar machen)
- ✅ vorhanden: SECURITY.md, CoC, Templates, CHANGELOG, SBOM+Provenance, Trivy-Gate, CI 553/553.
- Ergänzen: Release-Kadenz-Aussage im README („monatliche Minor-Releases, Security-Fixes
  sofort"), `cosign`-Signatur der Images (klein, Actions-Step), Upgrade-Doku (0.x → 0.x+1).
- Optional stark: externes Mini-Audit nur über die Auth/MCP-Boundary (bezahlbar, zitierbar).

### L5 — Der Launch selbst
- Reihenfolge: awesome-selfhosted-PR → Docker-MCP-/Glama-/MCP-Verzeichnisse (PR #8867 läuft)
  → Show HN („Show HN: Whiskers — let an AI run your Docker fleet, with hard guardrails")
  → r/selfhosted + r/homelab → Blogpost „Warum Guardrails im Code, nicht im Prompt".
- Messlatte Phase 0: 500+ Stars, 3+ externe Issues/PRs, erste Community-Rückmeldungen.

---

## Phase 1 — Portainer-K.-o.-Punkte schließen (~4–6 Wochen, parallelisierbar)

Die vier Stellen, an denen wir heute im 15-Minuten-Vergleichstest verlieren:

| # | Feature | Warum | Skizze | Größe |
|---|---|---|---|---|
| P1 | **Live-Docker-Events** (F6) | Portainer zeigt Zustandswechsel live, wir pollen | `MonitorEventsAsync` je Server → SignalR-Push + Event-Ringpuffer; Reconnect an Self-Healing-ConnMgr | M |
| P2 | **Ressourcen-Limits editieren** (F7) | Anzeigen-aber-nicht-ändern wirkt halbfertig | `ContainerUpdateParameters`; Socket-Proxy-Whitelist `POST /containers/{id}/update` nachziehen | S |
| ~~P3~~ | ~~**Server-Gruppen & Tags** (F9)~~ ✅ **DONE 2026-07-12** (`28834a2`) | Ab 5 Servern Alltags-Schmerz | `ServerConfig.Group/Tags` + Add/Edit-Form + Servers-Spalte + Dashboard-Filter + `list_servers`-`tag`-Param; 2 Tests | S |
| P4 | **Per-Server-RBAC light** | „Praktikant darf nur Staging" ist DIE KMU-Frage | `RoleEntry` + optionale `ServerIds`; Enforcement zentral im bestehenden Rollen-Check | M |
| P5 | **Volume-Datei-Browser** | Häufigster Portainer-Handgriff | One-Shot-Helper-Container (`ls`/`cat`/Download via Exec); Pfad-Traversal-Review! | M |

Bewusst NICHT: Edge-Agent/Hunderte Nodes, Swarm, ACI, LDAP, Multi-Tenancy, K8s-Vollmanagement.

---

## Phase 2 — Coolify-Angriff: „PaaS-lite" (~6–8 Wochen)

Coolifys Existenzgrund ist „App-URL rein → läuft mit HTTPS". Wir bauen keinen Heroku-Klon,
sondern schließen genau die zwei Lücken, die Nutzer zu Coolify treiben:

### C1 — One-Click-Expose mit TLS (der größte einzelne Hebel im ganzen Plan)
- **Kein eigener Proxy-Eigenbau.** Whiskers managt eine **Caddy-Instanz** (bevorzugt; Traefik
  als Option) auf dem Zielserver: „Container exponieren" → Domain eintragen → Whiskers schreibt
  die Route (Caddy-API/Labels), Let's Encrypt macht den Rest. Status + Zertifikat im UI.
- Damit ist der Weg „Repo → Git-Deploy → mit HTTPS erreichbar" komplett in Whiskers — Coolifys
  Kern-Story, aber mit Flotten-Ops, CVE-Scan und AI-Governance drumherum.

### C2 — Git-Deploy v2
- **Dockerfile-Pfad** ergänzen (heute compose-only): Repo ohne Compose → `docker build` + Run
  wie im Bereitstellen-Formular. Optional später nixpacks — NICHT jetzt.
- **One-Click-Datenbanken**: App-Store-Templates (Postgres/MySQL/Redis/Mongo) bekommen
  „mit Backup-Zeitplan anlegen" — verdrahtet VolumeBackups + Scheduler automatisch. Das ist
  Coolifys zweites Zugpferd und bei uns zu 80 % vorhanden, nur nicht verkabelt.
- **Deploy-Historie + Redeploy/Rollback** pro Git-App (Snapshot-Muster vom Auto-Update wiederverwenden).

### C3 — App-Store ausbauen
- Von ~10 auf ~40 kuratierte Templates (die r/selfhosted-Top-Liste: Vaultwarden, Immich,
  Paperless, Jellyfin, Nextcloud, Uptime-Kuma …), jedes mit sinnvollen Defaults + Backup-Haken.
- Community-Templates: Templates aus Git-Repo laden (JSON-Schema) statt PR-Zwang.

---

## Phase 3 — Den Graben ausbauen (AI-native; laufend, je Item 1–3 Wochen)

Hier kann uns keiner folgen, ohne seine Architektur umzubauen:

1. **Approval-Gate für externe MCP-Calls** (P2-Item aus missingFeatures): Approval-Hold in die
   MCP-Pipeline vor Tool-Dispatch — dann gilt die Human-in-the-Loop-Story für JEDEN Client
   (Claude Code, Cursor …), nicht nur den In-Process-Agenten. Architektur-relevant, zuerst.
2. **Echte Diffs in der Freigabe-Karte** („dieser Befehl ändert X → Y") — macht Approvals von
   „Klick ok" zu informierter Entscheidung; Demo-Gold.
3. **Fleet-Brief**: täglicher AI-Digest (neue CVEs, verfügbare Updates, Anomalien, Empfehlungen)
   über die vorhandenen Notification-Kanäle. Klein, aber jeder Screenshot davon ist Marketing.
4. **Runbook-Rezepte**: AI-Trigger-Vorlagen für die 10 häufigsten Incidents (Restart-Loop,
   Disk voll, ungesund nach Update → Rollback vorschlagen). Zeigt Agent-Wert ohne Prompt-Bastelei.
5. **Integration-Guides**: „Whiskers + Claude Code", „Whiskers + Cursor", „Guardrails-Rezepte"
   als Doku-Kapitel + Blogposts. SEO auf „docker mcp server", „ai devops agent self-hosted".

---

## Was wir explizit NICHT bauen (Anti-Backlog)

Multi-Tenancy · LDAP/SAML (→ Keycloak-Bridge dokumentieren) · Edge-Flotten · Swarm ·
K8s-Objekt-Vollmanagement · Buildpack-Engine · eigener Reverse-Proxy-Code · HA/Multi-Replika
(bleibt Single-Replica by design bis echte Nachfrage).

## Messlatte

- Phase 0: Launch durch; 500+ Stars; erste externe Contributor.
- Phase 1: kein K.-o.-Punkt mehr im 15-Minuten-Vergleich mit Portainer CE (Selbsttest + 2 externe Reviewer).
- Phase 2: „Repo → HTTPS-App" in < 5 Minuten, im Video belegt; App-Store ≥ 40 Templates.
- Phase 3: ≥ 3 veröffentlichte AI-Ops-Demos; MCP-Verzeichnis-Listings live; „whiskers" als
  Antwort unter „AI + Docker + self-hosted"-Fragen ohne eigenes Zutun.

## Abhängigkeiten & Reihenfolge

L1–L4 parallel → L5 (Launch). Danach Phase 1 und Phase 3.1 parallel (verschiedene Bereiche);
C1 vor C2 (Expose ist Voraussetzung der End-to-End-Story). 2FA (L3) und Approval-Gate (3.1)
berühren die Off-Limits-Auth-/MCP-Zone → je ein eigener PR mit User-Go.
