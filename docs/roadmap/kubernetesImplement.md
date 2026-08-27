# kubernetesImplement.md — Kubernetes NEBEN Docker

> **Ziel:** Zwei getrennte Tracks, die oft verwechselt werden — hier bewusst auseinandergezogen:
> - **Track A: Whiskers AUF Kubernetes betreiben** (Helm-Chart, damit KMU mit vorhandenem Cluster Whiskers deployen können). → **P0, unabhängig, zuerst.**
> - **Track B: Kubernetes-Workloads MIT Whiskers verwalten** (k3s-Cluster neben Docker-Hosts im selben Dashboard — README-Roadmap „lightweight Kubernetes (k3s) support"). → **P1, großes Refactoring, danach.**
>
> Ist-Stand (verifiziert): **Null K8s-Code im Repo** (nur ein Roadmap-Satz im README). Kein Health-Endpoint, hartkodierte `/app/data`-Pfade, SQLite, Blazor-Server-Sticky-Sessions, privilegierte nsenter-Operationen — alles relevant für beide Tracks.

---

## Track A — Whiskers auf Kubernetes deployen (Helm-Chart)

### A.0 Architektur-Entscheidungen (getroffen, nicht neu diskutieren)

1. **Single-Replica by design für 1.0.** Blazor Server (stateful SignalR-Circuits), 8+ Hosted-Service-Loops ohne Leader-Election, JSON-File-Stores mit Prozess-Cache → `replicas: 1`, `strategy: Recreate`. Das ist ehrlich, dokumentiert und für die Zielgruppe (KMU-Kontrollebene) völlig ausreichend. **HA ist ein Post-1.0-Thema** (braucht: Postgres ✓ via stableDB, Stores→DB via changeme C7, Leader-Election, Redis-Backplane — NICHT jetzt).
2. **Kein Docker-Socket-Zwang im Pod.** Auf K8s gibt es keinen sinnvollen „lokalen Docker-Host“; der Default-„local“-Server entfällt dort. Whiskers auf K8s verwaltet REMOTE-Docker-Hosts (SSH/TCP-mTLS/Mesh) — genau der Use-Case „Kontrollebene im Cluster, Docker-Flotte draußen“. Optional kann ein Docker-Host per TCP eingebunden werden.
3. **VPN im Sidecar oder gar nicht:** `VPN_PROVIDER=none` ist der Chart-Default; Tailscale als optionaler, dokumentierter Sidecar (offizielles `tailscale/tailscale`-Image, `TS_USERSPACE=true` → kein `/dev/net/tun`, kein NET_ADMIN nötig — Userspace-Networking reicht für ausgehende Verbindungen zur Flotte).
4. **DB:** Chart unterstützt SQLite (PVC, Default) und externes Postgres (`stableDB.md` — Voraussetzung nur für die Postgres-Option, nicht für das Chart selbst).

### A.1 Voraussetzungen im App-Code (VOR dem Chart umsetzen)

| # | Änderung | Quelle |
|---|---|---|
| V1 | `/healthz` (liveness) + `/readyz` (readiness) Endpoints | `changeme.md` C11 — exakt wie dort spezifiziert |
| V2 | `WHISKERS_DATA_DIR` statt hartkodiertem `/app/data` | `changeme.md` C1 |
| V3 | Graceful Shutdown: Hosted-Loops beenden < 5 s nach SIGTERM; `terminationGracePeriodSeconds: 30` | `changeme.md` C11 |
| V4 | „Local"-Server optional machen: `ServerConfigService` erzeugt den Default-local-Eintrag nur, wenn der Docker-Socket existiert bzw. `WHISKERS_DISABLE_LOCAL_DOCKER=true` nicht gesetzt ist. UI zeigt sonst den Empty-State aus `outOfTheBox.md` W3 | neu |
| V5 | Non-root lauffähig: Image defaultet heute auf root; für K8s `runAsUser: 10001` erzwingen — prüfen, dass keine Runtime-Pfade außerhalb `WHISKERS_DATA_DIR` beschrieben werden (Dockerfile legt uid 10001 bereits an; hardened-Compose beweist, dass die App als 10001 läuft) | Dockerfile |

### A.2 Das Chart (`deploy/helm/whiskers/`)

Struktur (Standard-Helm, keine Exoten):
```
deploy/helm/whiskers/
  Chart.yaml            # appVersion an Release-Tag gekoppelt (CI ersetzt)
  values.yaml
  templates/
    deployment.yaml     # replicas 1, strategy Recreate, probes, securityContext
    service.yaml        # ClusterIP :8080
    ingress.yaml        # optional; Anmerkungen für WebSocket/SignalR!
    pvc.yaml            # data-Volume (SQLite, Keys, JSON-Stores, Certs)
    secret.yaml         # optional: vaultKey, dbConnection (nur wenn nicht existingSecret)
    serviceaccount.yaml
    _helpers.tpl
  README.md             # Install-Beispiele: kind, k3s, mit/ohne Ingress, Postgres
```

**Wichtige values (Auszug, mit Defaults):**
```yaml
image: { repository: ghcr.io/lupusmalusdeviant/whiskers, tag: "" }   # "" = appVersion
persistence: { enabled: true, size: 2Gi, storageClass: "" }
database:
  provider: sqlite                  # sqlite | postgres
  existingSecret: ""                # Key: connectionString
vault: { existingSecret: "" }       # Key: vaultKey; leer => Wizard-generiert (PVC)
auth: { adminEmail: "", oidc: {...} }
ingress:
  enabled: false
  className: ""
  annotations: {}                   # README: nginx braucht proxy-read-timeout etc.
  hosts: []
tailscaleSidecar: { enabled: false, authKeySecret: "" }
localDocker: { enabled: false }     # true mountet /var/run/docker.sock (Warnhinweis!)
resources: { requests: {cpu: 250m, memory: 384Mi}, limits: {memory: 1Gi} }
```

**Deployment-Details, die ein schwächeres Modell falsch machen würde:**
- **Probes:** `readinessProbe: /readyz` (initialDelay 10 s, period 10 s), `livenessProbe: /healthz` (initialDelay 30 s, **period 30 s, failureThreshold 5** — Blazor-Apps unter Last nicht totprüfen), `startupProbe: /healthz` (failureThreshold 30 × 2 s — erste Migration kann dauern).
- **SignalR/WebSockets über Ingress:** Session-Affinity ist bei replicas=1 egal, aber Timeouts nicht: README muss für ingress-nginx `nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"` + `proxy-send-timeout` dokumentieren, sonst reißen Circuits nach 60 s. Für Traefik: nichts nötig.
- **ForwardedHeaders:** Whiskers vertraut RFC1918 + Loopback + Tailscale-CGNAT (`ForwardedHeaders:TrustedNetworks`). Pod-CIDRs liegen i. d. R. in RFC1918 → funktioniert; ABER Cluster mit anderen Pod-CIDRs brauchen ein value `trustedProxyCidrs` → ENV. Ins Chart-README.
- **securityContext:** `runAsNonRoot: true`, `runAsUser: 10001`, `fsGroup: 10001` (PVC-Ownership!), `allowPrivilegeEscalation: false`, `capabilities: {drop: [ALL]}`, `seccompProfile: RuntimeDefault`. `readOnlyRootFilesystem: true` + `emptyDir` auf `/tmp` (hardened-Compose beweist Machbarkeit).
- **PVC + Recreate:** SQLite + `Recreate`-Strategie verhindert zwei Writer beim Rollout. Bei `database.provider=postgres` bleibt trotzdem Recreate (JSON-Stores liegen weiter auf dem PVC, bis changeme C7 fertig ist).
- **Secrets:** `vault.existingSecret`/`database.existingSecret` bevorzugen; Chart erstellt eigene Secrets nur als Convenience. Niemals Secrets in values.yaml-Beispielen mit echten Werten.

### A.3 CI & Distribution
- Chart-Lint + `helm template`-Golden-Tests in CI; Release-Pipeline (aus `outOfTheBox.md` W2) pusht das Chart als **OCI-Artefakt** nach `ghcr.io` (`helm install whiskers oci://ghcr.io/lupusmalusdeviant/charts/whiskers`) — kein separates Chart-Repo-Hosting nötig.
- Smoke-Test-Job: `kind`-Cluster → `helm install` → auf `/readyz` warten → Login-Seite per curl prüfen.

### A.4 Definition of Done (Track A)
> 🟢 **Chart + V4 + CI gebaut** (Commit `eda50e7`, 2026-07-11): `deploy/helm/whiskers/` wie in §A.2 spezifiziert (Recreate/1 Replica, restricted-PodSecurity-Kontext, Probes inkl. lazy Liveness, PVC, Postgres-/Vault-Secrets, Ingress+WebSocket-Doku, Userspace-Tailscale-Sidecar, localDocker-Opt-in mit Warnung, `trustedProxyCidrs`); V4 (`WHISKERS_DISABLE_LOCAL_DOCKER` + Socket-Existenz-Gate, 5 Tests) und V5 (Chart erzwingt uid 10001) umgesetzt; helm-Job in `release.yml` (Lint + Package + OCI-Push nach ghcr.io/…/charts), `chart-ci.yml` (Lint + Render-Invarianten, kind-Smoke als manueller Job). Lokal verifiziert mit helm 3.16: Lint clean, Default- + Voll-Varianten-Render.
- [ ] `helm install` auf kind UND k3s: Pod ready, Wizard erreichbar, Remote-Docker-Host (SSH) hinzufügbar. **← OFFEN: braucht das erste veröffentlichte Image (v*-Tag), dann manuellen kind-Smoke-Job ausführen**
- [ ] Pod-Restart verliert keine Daten (PVC), Rollout `helm upgrade` läuft ohne PVC-Konflikt (Recreate). **← OFFEN (mit kind-Smoke)**
- [x] Läuft unter restricted PodSecurity-Standard (non-root, no caps) — MIT `localDocker.enabled=false`. *(im Template erzwungen + per Render-Invariante in chart-ci abgesichert)*
- [ ] Postgres-Variante getestet (Chart + `stableDB.md`-Provider). **← OFFEN (Render verifiziert, Live-Test mit Cluster)**
- [x] Chart-README beantwortet: Ingress-Timeouts, Tailscale-Sidecar, Secret-Handling, Backup des PVC.

---

## Track B — Kubernetes-Workloads mit Whiskers verwalten (k3s-Fokus)

### B.0 Architektur-Entscheidung: Provider-Seam statt Parallel-App

Heute ist `IDockerService` (994-Zeilen-Implementierung, von 17 Dateien injiziert) die einzige Workload-Abstraktion, keyed by `serverId`. **Entscheidung:** Wir führen ein backend-neutrales Seam ein und mappen K8s-Konzepte auf die bestehende Container-UX, statt eine zweite UI zu bauen:

```
                        ┌─ DockerWorkloadProvider (wraps heutigen DockerService)
IWorkloadProvider ──────┤
  (pro ServerConfig)    └─ KubernetesWorkloadProvider (neu, KubernetesClient)
```

- `ServerConfig` bekommt `ConnectionType.Kubernetes` + Felder `KubeconfigVaultRef` (kubeconfig verschlüsselt im Vault!, NICHT als Datei), `KubeContext`, `KubeNamespaces` (Allowlist, leer = alle sichtbaren).
- **Konzept-Mapping (die zentrale Design-Tabelle):**

| Whiskers-Konzept | Docker | Kubernetes |
|---|---|---|
| „Server“ | Docker-Host | Cluster (oder Cluster+Namespace-Scope) |
| Container-Liste | Container | **Pods** (Container darin als Detail) |
| Compose-Projekt-Gruppe | compose labels | Deployment/StatefulSet/DaemonSet (Owner-Referenz) |
| Start/Stop | start/stop | Scale auf N/0 (Deployments); Pods: delete (Recreate durch Controller) — UI muss das ehrlich benennen! |
| Restart | restart | `kubectl rollout restart`-Äquivalent (Pod-Template-Annotation patchen) |
| Logs | container logs | Pod-/Container-Logs (Core V1) |
| Exec/Terminal | exec attach | Pod exec (WebSocket-Stream — passt in die bestehende xterm-Infrastruktur) |
| Stats | docker stats | metrics-server API (`PodMetrics`); wenn fehlt: „Metriken nicht verfügbar“-Empty-State |
| Deploy (Compose) | compose up | **Nicht übersetzen.** Stattdessen: Manifest-Apply (YAML-Editor, server-side apply) als eigener Deploy-Pfad |
| Image-Update | pull+recreate | `set image` auf Deployment (Rollout inklusive Rollback — hier ist K8s dem Docker-Pfad voraus, `changeme.md` C12 lässt grüßen) |

### B.1 Schritt 1 — Seam-Refactoring (reines Refactoring, kein K8s)

1. `IDockerService` methodenweise sichten und in `Services/Workloads/IWorkloadProvider.cs` das backend-neutrale Subset definieren (List/Inspect/Start/Stop/Restart/Logs/Exec/Stats/Events). Docker-only-Operationen (Netzwerke verwalten, Volume-Backups, Host-Shell via nsenter, Image-Pull) bleiben auf `IDockerService` bzw. wandern in ein `IDockerExtensions`-Capability-Interface.
2. `DockerWorkloadProvider : IWorkloadProvider` als dünner Adapter über den bestehenden `DockerService` (KEINE Logik-Änderung; `changeme.md` C3-Split vorher erledigen macht das leichter, ist aber nicht Bedingung).
3. Dispatch: `IWorkloadProviderFactory.GetForServer(serverId)` — wählt anhand `ServerConfig.ConnectionType`. Konsumenten migrieren schrittweise (Dashboard + ContainerDetail zuerst; MCP-Tools zuletzt).
4. **Capability-Flags** am Provider (`SupportsCompose`, `SupportsHostShell`, `SupportsNetworks`, `SupportsResourceEdit`, …): UI und MCP-Tools blenden Aktionen aus, die das Backend nicht kann — verhindert 30 `if (isK8s)`-Verzweigungen. Muster: `IVpnProvider`/`IImageSearchProvider` (`RoadToSAP.md` §4).

**Dieser Schritt ist der Löwenanteil und für sich allein wertvoll** (Testbarkeit via Fake-Provider → `outOfTheBox.md` W4 Demo-Modus).
> 🟢 **B1 erledigt** (Commit `ae7b206`, 2026-07-11): `Services/Workloads/` — `IWorkloadProvider` (List/Get/Start/Stop/Restart/Logs/Stats, pro Server gebunden statt serverId-Parameter), `WorkloadCapabilities`-Flags inkl. ehrlicher `StartStopSemantics`, `DockerWorkloadProvider` (reine Delegation), `IWorkloadProviderFactory.GetForServer` (Dispatch über ConnectionType), `FakeWorkloadProvider` (Tests + W4-Demo-Basis), 7 Tests. **Abweichung:** Exec/Events NICHT im Seam-v1 (IDockerService hat kein Container-Exec — das lebt in der Terminal-Schicht; kommt mit B.3). C3 lief als eigener PR davor (`db84e36`).

### B.2 Schritt 2 — KubernetesWorkloadProvider

- NuGet `KubernetesClient` (offizieller .NET-Client). Client-Cache pro Server im Muster von `DockerConnectionManager` (inkl. Self-Healing-Reconnect).
- Kubeconfig aus Vault laden → `KubernetesClientConfiguration.BuildConfigFromConfigObject` (in-memory, nie auf Platte).
- Implementierungsreihenfolge (je PR): List Pods+Owner-Gruppierung → Logs → Exec (WebSocket→xterm) → Start/Stop/Restart (Scale/rollout-restart) → Stats (metrics-server, optional) → Events (Watch → `ContainerHub`, deckt `missingFeatures.md` F6 für K8s gleich mit ab).
- RBAC-Doku: mitgeliefertes Manifest für einen minimalen ServiceAccount (`deploy/k8s/whiskers-agent-rbac.yaml`) — get/list/watch auf pods/deployments + pods/log + pods/exec + optional patch auf deployments. **Onboarding-Flow „K8s-Cluster hinzufügen“:** kubeconfig-Paste ODER „ServiceAccount-Token generieren“-Anleitung im Dialog.
> 🟢 **B2 erledigt** (Commit `6bf3a7b`, 2026-07-11): `Services/Workloads/Kubernetes/` — `KubernetesClientCache` (in-memory aus Vault-kubeconfig `kubeconfig:{serverId}`, nie auf Platte; self-healing Invalidate), `KubernetesWorkloadProvider` (Pods → ContainerInfo, Id `{ns}/{pod}`, Owner-Gruppierung über das Compose-Label mit ReplicaSet→Deployment-Hash-Trim ohne Extra-API-Call, Logs tail/since, Stop=Scale-0/Bare-Pod-Delete, Start=Scale-1 nur von 0, Restart=Rollout-Annotation; DaemonSet/Bare-Pod lehnen ehrlich ab), `ConnectionType.Kubernetes` ans Enum-ENDE (INT-persistiert), `KubeContext`+`KubeNamespaces` in ServerConfig, Docker-Pfad-Guards (ListAll/SystemInfo/Metrics/CVE skippen K8s; DockerConnectionManager wirft laut), Servers-UI (kubeconfig-Paste, Vault-Pflicht, Seam-Verbindungstest, Vault-Cleanup bei Delete), Dashboard-Merge + Aktions-Routing + ausgeblendete Docker-only-Buttons, RBAC-Manifest + Anleitung `deploy/k8s/`, 18 Tests. **Abweichungen:** Exec (WebSocket→xterm) + Stats (metrics-server) + MCP-Tools = B.3; **noch nicht gegen einen echten Cluster verifiziert** (kein Cluster auf der Dev-Box) — Unit-Tests + DI-Boot + UI-Smoke liefen.

### B.3 Schritt 3 — UI + MCP
- `Servers.razor`: neuer Typ „Kubernetes-Cluster“ (Verbindungstest = `GET /version`).
- Dashboard: Cluster erscheinen als Server-Karten; Pod-Gruppen wie Compose-Projekte. Capability-Flags steuern Buttons.
- MCP: bestehende Tools (`list_containers`, `get_container_logs`, `restart_container`, …) funktionieren über das Seam automatisch für K8s-Server; NUR wo Semantik abweicht (restart = rollout) den `[Description]`-Text präzisieren. Neue Tools sparsam: `k8s_apply_manifest` (Admin-Level, Guardrail-pflichtig), `k8s_scale`.
- CVE-Scanning: Trivy kann K8s-Images scannen — Ausbaustufe, NICHT in v1 von Track B.

### B.4 Nicht-Ziele (Track B, explizit)
- Kein Helm-Release-Management in der UI, keine CRD-Verwaltung, kein Cluster-Provisioning, kein Multi-Cluster-Sync — das ist Lens/Rancher-Terrain. Whiskers-Versprechen: **eine Flotte aus Docker-Hosts UND k3s-Clustern in EINEM einfachen Dashboard mit MCP/Agent-Zugriff.** Das kann niemand sonst.

### B.5 Definition of Done (Track B)
- [ ] k3s-Cluster via kubeconfig hinzufügen → Pods im Dashboard, Logs + Exec-Terminal funktionieren.
- [ ] Restart/Scale über UI und über MCP-Tool (Agent mit Guardrails getestet).
- [ ] Docker-Funktionalität 100 % unverändert (Regressionslauf der bestehenden Tests + manueller Smoke auf Badwolf-Staging — NICHT direkt produktiv deployen).
- [ ] Fake-`IWorkloadProvider` existiert und trägt Demo-Modus + Unit-Tests.

---

## Abhängigkeiten / Reihenfolge

```
changeme C1+C11 (+V4,V5) ─→ Track A (Helm)        [unabhängig von stableDB; Postgres-Option optional]
stableDB.md ──────────────→ Track A Postgres-Option
changeme C3 (empfohlen) ──→ Track B Schritt 1 (Seam) ─→ Schritt 2 (Provider) ─→ Schritt 3 (UI/MCP)
RoadToSAP.md Modul-Muster ─→ Track B als „Kubernetes-Modul“ registrieren (wenn Modul-Framework fertig; sonst klassisch registrieren und später umziehen)
```
**Track A und Track B sind unabhängig voneinander schedulebar.** Für 1.0: Track A zwingend, Track B mindestens Schritt 1+2 (List/Logs/Exec), Schritt 3 komplett ist 1.x-fähig.
