# RoadToSAP.md — Whiskers als modulares Base-Framework

> **Vision („SAP-Gedanke“):** Whiskers wird ein schlanker **Core** (Server-Registry, Verbindungen, Auth, Persistenz, UI-Shell, MCP-Host) plus **Module**, die Features beisteuern und sich einzeln an-/abschalten lassen — Interface-first, so dass künftige Features (Kubernetes, Git-Deploy, neue Cloud-Provider, Community-Module) andocken statt einwachsen.
>
> **Was das NICHT heißt:** kein dynamisches Plugin-Laden fremder DLLs zur Laufzeit für 1.0 (Sicherheits- und Stabilitätsrisiko). Module sind zunächst **In-Assembly-Module mit erzwungener Disziplin**; die Assembly-Trennung und externe Plugins sind Phase 2/3.

---

## 1. Ist-Zustand (verifiziert — die Basis ist besser als gedacht)

**Vorhanden und nutzbar:**
- Feature-gefolderte Services: 32 Ordner, 175 Dateien, **75 Interfaces** — Interface-first ist weitgehend Realität.
- **4 echte Provider-Seams als Vorbild:** `IVpnProvider` (das Referenzmuster: `Id`, `DisplayName`, `IsAvailableAsync`, Multi-Registration, Auswahl per Settings), `IImageSearchProvider`, `IAgentLlmProvider`+Factory, Notification-Kanäle (unvollständig, siehe unten).
- Typisierte Options-Klassen (~20 in `Configuration/`, je `SectionName`), UI-editierbare Settings-Layer (`app-settings.json`, reloadOnChange).
- MCP-Tools als attributierte Klassen (`[McpServerToolType]`) mit zentraler Permission-Matrix (`DefaultToolLevels`).

**Fehlt (die eigentliche Arbeit):**
- **Kein Modul-Framework:** 704-Zeilen-`Program.cs` registriert alles inline; keine einzige `IServiceCollection`-Extension; 8 handverdrahtete `InitializeAsync()`-Aufrufe.
- `Enabled`-Flags werden nur IN Seiten geprüft — DI, Nav, Routen, MCP-Tools sind IMMER aktiv.
- `NavMenu.razor` = hartkodierte Linkliste; `Settings.razor` = 1042-Zeilen-Monolith aller Feature-Settings.
- Zwei Shared-Kernel-Hubs, an denen fast alles hängt: `IDockerService` (17 Konsumenten) und `IHostCommandExecutor` (12 Konsumenten).
- Anti-Muster: Service-Locator in `CveMonitorService`/`CompositeNotificationService`; Cloud-Dispatch als Enum-Switch; Composite-Notifications mit 8 hart verdrahteten Kanälen.

---

## 2. Zielbild

### 2.1 Schichten

```
┌─────────────────────────── Module (an-/abschaltbar) ───────────────────────────┐
│ Cve · Notifications(+Kanäle) · Agent · AiChat · CloudControl(+Provider) ·      │
│ NginxMgmt · SystemdMgmt · Firewall · SslCerts · Terminal · Scheduler ·         │
│ VolumeBackups · Webhooks · LogMonitor · ImageUpdate/AutoUpdate ·               │
│ Deployment/AppStore(+ImageSearch-Provider) · (später: Kubernetes, GitDeploy)   │
├──────────────────────────── Core (immer an) ────────────────────────────────────┤
│ ServerRegistry (ServerConfig, ConnectionMgmt, Onboarding) · Workloads-Seam     │
│ (IDockerService/IWorkloadProvider) · HostCommand-Seam · Auth/Rollen/Whitelist  │
│ · Persistenz (DbContext, JsonFileStore, Vault, DataProtection) · Metrics-Core  │
│ · UI-Shell (Layout, Nav-Registry, Dashboard-Rahmen) · MCP-Host · Health/Setup  │
└─────────────────────────────────────────────────────────────────────────────────┘
```
**Abgrenzungsregel:** Core = alles, ohne das kein Modul funktioniert ODER was Sicherheitsgrenze ist (Auth, MCP-Host, Vault). Module dürfen Core-Interfaces konsumieren, NIE umgekehrt. Module dürfen einander nur über deklarierte optionale Contracts kennen (z. B. „Cve nutzt Notifications, wenn vorhanden“ — via `INotificationService`, das im Core-Contract-Paket liegt und eine No-op-Implementierung hat, wenn das Notifications-Modul aus ist).

### 2.2 Der Modul-Contract (Kernstück — exakt so bauen)

```csharp
// src/Whiskers/Modules/IWhiskersModule.cs
public interface IWhiskersModule
{
    string Id { get; }                 // "cve", "notifications", stabil, kebab-case
    string DisplayName { get; }        // via IStringLocalizer sobald i18n (F2) da ist
    bool EnabledByDefault { get; }
    IReadOnlyList<string> DependsOn { get; }              // Modul-Ids, z. B. Agent → ["notifications"]? nein: leer halten, soft-deps über No-op-Contracts
    void ConfigureServices(IServiceCollection services, IConfiguration config);
    IReadOnlyList<NavItem> NavItems { get; }              // record NavItem(string Href, string LocKey, string Icon, string Group, AppRole MinRole, int Order)
    IReadOnlyList<Type> McpToolTypes { get; }             // [McpServerToolType]-Klassen des Moduls
    Task InitializeAsync(IServiceProvider sp, CancellationToken ct); // ersetzt die 8 Hand-Aufrufe
}
```

```csharp
// Program.cs schrumpft auf:
var modules = ModuleCatalog.DiscoverEnabled(builder.Configuration);
// DiscoverEnabled: statische Liste aller Module (KEINE Assembly-Reflection in Phase 1,
// eine explizite List<IWhiskersModule> in ModuleCatalog.cs — auffindbar, debugbar, AOT-freundlich),
// gefiltert nach Features:<id>:Enabled (Default = EnabledByDefault), Dependency-Check mit klarer Fehlermeldung.
foreach (var m in modules) m.ConfigureServices(builder.Services, builder.Configuration);
builder.Services.AddSingleton<IModuleRegistry>(new ModuleRegistry(modules));
```

**Feature-Flags:** neue Config-Sektion `Features:{moduleId}:Enabled` (ENV: `Features__cve__Enabled=false`), UI-Verwaltung in Settings → „Module“ (schreibt in `app-settings.json`; **Änderung erfordert Neustart** — ehrlich anzeigen, kein Hot-Toggle in Phase 1! Hot-Toggle wäre ein Circuit-/DI-Albtraum).

### 2.3 Was ein deaktiviertes Modul bedeutet (alle vier Ebenen!)
1. **DI:** `ConfigureServices` wird nicht aufgerufen → Hosted Services laufen nicht (heute laufen CVE-Scans etc. immer!).
2. **Nav:** `NavMenu.razor` rendert aus `IModuleRegistry.NavItems` (gruppiert nach `Group`, gefiltert nach Rolle) — der Hardcode fällt weg.
3. **Routen:** Blazor-Seiten deaktivierter Module → zentraler `ModuleGuard` (Wrapper-Komponente analog `RoleGuard.razor`): rendert „Modul deaktiviert“-Hinweis. (Routen wirklich aus dem Router entfernen ist mit Blazor-Assembly-Routing unverhältnismäßig; der Guard reicht, weil ohne DI-Registrierung ohnehin nichts funktionieren würde — Guard liefert die saubere Meldung statt DI-Exception.)
4. **MCP:** Tool-Registrierung iteriert `modules.SelectMany(m => m.McpToolTypes)` statt 11 fixer `.WithTools<>()`-Zeilen. `AgentToolRegistry` filtert zusätzlich auf registrierte Module (heute: Assembly-Reflection → auf ModuleRegistry umstellen). Deaktiviertes Modul = Tools existieren nicht (weder extern noch für den Agenten).

---

## 3. Implementierungs-Phasen

### Phase 0 — Vorbereitende Helfer (1 PR)
- `AddSingletonWithInterface<TImpl,TIface>()`-Extension (ersetzt das 14× Dual-Registration-Idiom), optional `.AsHostedService()`.
- `IInitializable { int Order; Task InitializeAsync(CancellationToken); }` + Start-Loop; die 8 handverdrahteten Init-Aufrufe umziehen. **Reihenfolge exakt konservieren** (Order-Werte = heutige Program.cs-Reihenfolge, als Kommentar begründen).
- `NavItem`-Record + `IModuleRegistry`-Gerüst (noch von einem „AllInOne“-Pseudo-Modul gespeist, Verhalten unverändert).

> 🟢 **Phase 0 erledigt** (Branch `feat/sap-phase0-scaffolding` auf `integration/welle1-foundation`, 2026-07-09, 3 Commits): **(1)** `ServiceCollectionExtensions.AddSingletonWithInterface[AndHostedService]` + 6 selbstständige Idiom-Stellen umgestellt — die 7 `AddHttpClient`-verzahnten Notification-/AiChat-Registrierungen bleiben bewusst für ihre Phase-1-Modul-Migration (§4: byte-gleich verschieben statt doppelt anfassen). **(2)** `IInitializable{int Order; InitializeAsync(ct)}` + Order-Loop ersetzt die 9 Hand-Aufrufe (Order 10..90 = heutige Reihenfolge, im Code kommentiert; 9 Services + 8 Interfaces implementieren es, `ct=default` hält bestehende Aufrufer/Tests kompilierbar; `AddInitializable<T>`-Forward). **(3)** `Modules/`-Gerüst (`NavItem`, `IModuleRegistry`/`ModuleRegistry`, `AllInOnePseudoModule` mit den heutigen 24 Nav-Einträgen) — als Singleton registriert, aber **inert** (NavMenu bleibt hartcodiert). DoD: build ✓, 298 Tests ✓, DI-Boot-Gate ✓ (Init-Order per Boot-Log bestätigt: 10→90). **Bewusst NICHT in Phase 0:** NavMenu/Settings-Umbau, `Features:<id>:Enabled`, MCP-Tool-Iteration, `IWhiskersModule`-Vollausbau — alles Phase 1.

### Phase 1 — Core-Extraktion + Pilotmodule (je Modul 1 PR, Reihenfolge = Entkopplungsgrad)

> 🟢 **Framework-PR erledigt** (Branch `feat/sap-phase1-framework` von `main`, 2026-07-09, 4 Commits, ungepusht): das Modul-Framework ist **verhaltensneutral** scharfgeschaltet. `IWhiskersModule`-Contract (§2.2 exakt) + `ModuleCatalog.DiscoverEnabled` (statische Liste, `Features:<id>:Enabled`-Gate, Dependency-Fail-fast); `Program.cs` entdeckt Module früh, ruft `ConfigureServices`, registriert MCP-Tools via `modules.SelectMany(McpToolTypes)` und baut die Nav-Registry aus den Modulen; `NavMenu.razor` rendert aus `IModuleRegistry` (Pure-Helper `NavLayout`, 4 Tests); `AllInOnePseudoModule` ist jetzt ein echtes `IWhiskersModule` (no-op `ConfigureServices`, trägt die 24 Nav-Einträge + 11 Tool-Klassen). **Bewusst NICHT hier:** `ModuleGuard` + `Settings.razor`-Split + `Features`-Toggle-UI + die erste echte Feature-Extraktion — das ist der **Terminal-PR** (nächster). DoD: build ✓, **302 Tests** ✓, Boot-Gate ✓ (67 MCP-Tools identisch), Render-Check ✓ (4 Gruppen + Links aus der Registry). Commits `dcc591e`, `b722e4d`, `045ce5d`, `e5284a1`.

> 🟢 **Terminal-PR erledigt** (Branch `feat/sap-phase1-terminal` von `main`, 2026-07-09, 2 Commits `d4bf05f`+`1a7891f`, ungepusht): erstes echtes Modul extrahiert. `Modules/Terminal/TerminalModule` (TerminalSettings-Binding + `ITerminalSessionManager` byte-gleich aus Program.cs); `IModuleRegistry.IsEnabled(id)` + `ModuleGuard`-Komponente. **DI-sicherer Guard-Pattern:** die `@page`-Dateien wurden dünne Route-Wrapper `<ModuleGuard><…View/></ModuleGuard>`, die interaktive Logik (mit `@inject`) liegt in `TerminalView`/`ServerTerminalView` → bei „aus" wird die View nie instanziiert, keine DI-Exception. Settings-Panel via `@if IsEnabled("terminal")` ausgeblendet. Per-Folder-README + `docs/modules/terminal.md`. DoD: build ✓, **305 Tests** ✓, Boot-Gate an (View rendert, MCP 67/DB intakt) + aus (`Features:terminal:Enabled=false` → App bootet, `/server-terminal`=200 mit „deaktiviert", keine DI-Exception; Settings-Panel weg) ✓.

Migrationsreihenfolge (vom saubersten zum verfilztesten — Erfolg früh sichern):
1. ✅ **Terminal** (Pilot #1, Muster etabliert — siehe Notiz oben).
2. 🟡 **Notifications** — **C9 ✅ committet** (`651fbe3` auf Branch `feat/sap-phase1-notifications`, gepusht, unmerged): `INotificationChannel` + `CompositeNotificationService` via `IEnumerable<>`, `NoopNotificationService` angelegt (ungenutzt). **Modul-Move offen** (verstreute Program.cs-Zeilen + 2 Verhängnisse: `ContainerNotificationPrefsService`=`IInitializable`, Log-Filter an `builder.Logging`) → bewusst für frischen Context, **exakter Schritt-für-Schritt-Plan in Memory `[[project_sap_phase1_modules]]`**. Empfehlung: `InAppNotificationStore` + `ContainerNotificationPrefsService` im Core lassen, nur Kanäle+Composite ins Modul.
3. **ImageSearch/AppStore + Deployment**, **VolumeBackups**, **Webhooks**, **Scheduler**, **LogMonitor** — mechanisch nach Muster.
4. **Nginx/Systemd/Firewall/SslCerts** — vier kleine „Host-Management“-Module (oder EIN Modul `host-management` mit vier Untersektionen — Entscheidung: **ein Modul**, sie teilen `IHostCommandExecutor` und die Zielgruppe schaltet sie zusammen ab).
5. **CVE** — dabei C8 (Service-Locator raus), Abhängigkeit auf Notifications über den Core-Contract.
6. **CloudControl** — dabei C10 (`ICloudProvider`-Seam, Hetzner/Hostinger als Provider IM Modul, OPT-12-CancellationTokens).
7. **ImageUpdate/AutoUpdate** (inkl. `changeme.md` C12-Rollback, wenn zeitlich passend).
8. **Agent + AiChat** — zuletzt (größtes Modul, 34 Dateien, aber bereits gut gekapselt; MCP-Tool-Brücke auf ModuleRegistry umstellen).

**Pro Modul-PR (Checkliste, immer identisch):**
- [ ] `Modules/<Name>/<Name>Module.cs` mit `ConfigureServices` (Registrierungen 1:1 aus Program.cs verschieben — NICHT umschreiben), NavItems, McpToolTypes.
- [ ] Optionen des Moduls binden im Modul, nicht in Program.cs.
- [ ] Seiten des Moduls in `ModuleGuard` wrappen; Settings-Abschnitt aus `Settings.razor` in `Modules/<Name>/Components/<Name>Settings.razor` extrahieren (`Settings.razor` wird zur Modul-Panel-Liste → löst nebenbei den 1042-Zeilen-Monolith, `changeme.md` C4-Schwester).
- [ ] `Features:<id>:Enabled=false` → App bootet, Nav-Eintrag weg, MCP-Tools weg, Hosted Services laufen nicht, DI-Boot-Gate grün (**Development + `ValidateOnBuild` booten — Projektregel!**).
- [ ] Per-Folder-README des Moduls (Projektregel), `docs/modules/<id>.md` Kurzbeschreibung.

### Phase 2 — Assembly-Trennung (nach 1.0, vorbereiten, nicht ausführen)
- Module nach `src/Whiskers.Modules.<Name>`-Projekten verschieben; Core-Contracts in `Whiskers.Core.Abstractions`. Der `IWhiskersModule`-Contract ist dafür bereits geschnitten (keine Program.cs-Interna im Interface!). Discovery bleibt statische Referenzliste.

### Phase 3 — Externe Module (Post-1.0, nur Leitplanken festhalten)
- Wenn je nötig: signierte NuGet-Pakete, `AssemblyLoadContext`, Permission-Manifest pro Modul (welche Core-Interfaces, welche MCP-Level). NICHT vorher generalisieren — YAGNI.

---

## 4. Leitplanken für die Umsetzung (How / How-not)

**How:**
- `IVpnProvider` ist das Referenzmuster für jedes Provider-Seam (Id + DisplayName + Availability + Multi-Registration + Auswahl per Settings). Bei jedem neuen Seam zuerst dort abschauen.
- Registrierungscode beim Umzug **byte-gleich** halten (Verschieben ≠ Refactoring). Refactorings (C8/C9/C10) nur da, wo dieses Dokument sie explizit dem Modul-PR zuordnet.
- Nach JEDEM Modul-PR: kompletter Testlauf + DI-Boot-Gate + manueller Smoke (Dashboard, das Modul selbst, ein MCP-Call).
- Soft-Dependencies zwischen Modulen IMMER über einen Core-Contract mit No-op-Default lösen (Beispiel Notifications). `DependsOn` nur für harte Fälle (sollte leer bleiben; wenn nicht, Design überdenken).

**How not:**
- **KEINE Assembly-Scanning-/Reflection-Discovery in Phase 1** — explizite Liste. Reflection versteckt Fehler bis zur Laufzeit; die statische Liste macht Code-Review und Trimming möglich.
- **KEIN Hot-Toggle** von Modulen zur Laufzeit (Neustart-Semantik klar anzeigen).
- **NICHT** `IDockerService`/`IHostCommandExecutor` in dieser Initiative zerschneiden — das ist `kubernetesImplement.md` Track B (Workload-Seam) bzw. `changeme.md` C3. Hier gelten sie als gegebene Core-Contracts.
- **KEINE** neuen öffentlichen Extension-APIs versprechen/dokumentieren, bevor Phase 2 die Assembly-Grenze real gemacht hat.
- **NICHT** alle Module in einem Branch migrieren — je Modul ein PR, dazwischen deploybar.

## 5. Abhängigkeiten
- Baut auf: `changeme.md` C2 (ist Phase 0/1 hiervon), C9/C10/C8 (den Modul-PRs zugeordnet).
- Liefert an: `kubernetesImplement.md` Track B (K8s als Modul), `missingFeatures.md` F5 (GitDeploy als Modul), F10 (MCP-Tool-Katalog aus ModuleRegistry generierbar), `outOfTheBox.md` (Wizard-Schritt „Module wählen“ als spätere Ausbaustufe — KMU-Profil = weniger Module an).
- Parallel möglich zu: `stableDB.md`, F2 (i18n — NavItems von Anfang an mit LocKeys bauen!).

## 6. Definition of Done
- [ ] `Program.cs` < 150 Zeilen; kein Service-Registrierungsblock mehr inline.
- [ ] Jedes der ~15 Module einzeln deaktivierbar; Matrix-Smoke: „alle an“, „alle aus“, „nur Core“ bootet jeweils sauber (automatisierter Test, der beide Extreme bootet).
- [ ] Nav, Settings-Seite, MCP-Toolliste sind vollständig registry-getrieben (Grep: kein Feature-Href mehr in `NavMenu.razor`).
- [ ] `docs/ARCHITECTURE.md` um Kapitel „Module System“ ergänzt + `docs/modules/`-Index; per-Folder-READMEs aktuell.
- [ ] Ein bewusst minimales Beispielmodul (`Modules/HelloWorld`, hinter `EnabledByDefault=false`) dient als lebende Doku für künftige Module.
