# stableDB.md — PostgreSQL als stabile Datenbank (statt/neben SQLite)

> **Ziel:** Whiskers soll wahlweise mit PostgreSQL laufen (Produktions-/KMU-Betrieb, K8s, Multi-Replica-Vorbereitung), SQLite bleibt der Zero-Config-Default für Single-Host-Installationen.
> **Aufwand:** ~2–4 Arbeitstage für ein Modell wie Opus 4.8. **Risiko:** niedrig — der EF-Core-Layer ist bereits sauber (keine Value Converter, kein Dapper, kein `FromSqlRaw`).

---

## 1. Ist-Zustand (verifiziert, Stand 2026-07-09)

- **Ein einziger DbContext:** `MetricsDbContext` in `src/Whiskers/Services/Persistence/MetricsDbContext.cs` mit **15 DbSets** (ContainerMetrics, ServerMetrics, AlertHistory, AuditLog, McpToolCalls, VolumeBackups, ScheduledTasks, TaskRunHistory, LogAlertRules, UpdatePolicies, UpdateHistory, Webhooks, WebhookLogs, CveFirstSeen, Notifications).
- **Provider hart verdrahtet** an zwei Stellen:
  - `src/Whiskers/Program.cs:177-180` → `options.UseSqlite("Data Source=/app/data/metrics.db")`, `ServiceLifetime.Transient`. Connection-String ist ein Literal, NICHT konfigurierbar.
  - `src/Whiskers/Services/Persistence/MetricsDbContextFactory.cs:15` (Design-Time-Factory für `dotnet ef`).
- **Genau eine Migration:** `Migrations/20260707164258_InitialCreate.cs` + Snapshot. Baseline-Logik dokumentiert in `docs/adr/0003-ef-core-migrations-baseline.md`.
- **SQLite-spezifische Artefakte** (die einzigen nicht-portablen Stellen):
  - `Services/Persistence/DatabaseInitializer.cs:40` — `LegacyHealSql` (~220 Zeilen `CREATE TABLE IF NOT EXISTS`, SQLite-DDL) für Legacy-`EnsureCreated`-Datenbanken.
  - `DatabaseInitializer.cs:53` — `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;`
  - `DatabaseInitializer.cs:85-91` — Sentinel-Check via `SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name`.
  - `Sqlite:Autoincrement`-Annotations + TEXT-DateTime-Spalten in `InitialCreate`.
- **Portables Raw SQL** (kann bleiben): `InAppNotificationStore.cs:73,81,153-155` (UPDATE/DELETE mit doppelt-gequoteten Bezeichnern, `LIMIT {0}`).
- **Was NICHT in der DB liegt** (bleibt unverändert): `servers.json`, `vault.json`, `roles.json`, `api-keys.json` etc. via `JsonFileStore<T>`; DataProtection-Keys unter `/app/data/keys`. → Postgres-Umstieg betrifft NUR die 15 Tabellen. (Die Migration der JSON-Stores in die DB ist ein separates Thema, siehe `changeme.md` und `kubernetesImplement.md`.)
- **Keine** Value Converter, keine Owned Types, keine nativen GUID-Spalten (alle Business-IDs sind `Guid.NewGuid().ToString("N")` als TEXT), kein `EnableRetryOnFailure`, kein Dapper.
- **Tests:** `DbMigrationBaselineTests.cs` und `CveAgePruneTests.cs` nutzen echte Temp-File-SQLite-DBs (bewusst, siehe Kommentar in `DbMigrationBaselineTests.cs:9-11`).

### Bekannte Verhaltensunterschiede SQLite ↔ Postgres (MÜSSEN behandelt werden)

| # | Unterschied | Betroffene Stelle | Fix |
|---|---|---|---|
| U1 | `LIKE` ist in SQLite ASCII-case-insensitive, in Postgres case-sensitive | `McpCallLogStore.cs:76-77` (`.Contains(actor)`, `.Contains(tool)`) | `.ToLower().Contains(x.ToLower())` ODER provider-abhängig `EF.Functions.ILike` — Erstere Variante wählen (provider-neutral, Tabelle ist klein/90-Tage-gedeckelt) |
| U2 | DateTime: SQLite speichert TEXT (lexikalischer Vergleich), Postgres native `timestamp` | alle Timestamp-Queries (`MetricsQueryService`, Retention-Pruning) | Unkritisch, da ausschließlich `DateTime.UtcNow` geschrieben wird — ABER: Npgsql wirft bei `DateTimeKind.Unspecified`/`Local` mit `timestamptz`. Lösung siehe Schritt 4. |
| U3 | Autoincrement: `Sqlite:Autoincrement` vs. Postgres Identity | alle `long Id`-PKs | Erledigt sich automatisch durch provider-eigene Migrationen (Schritt 3) |
| U4 | WAL-PRAGMA existiert in Postgres nicht | `DatabaseInitializer.cs:53` | Nur im SQLite-Zweig ausführen (Schritt 5) |
| U5 | `sqlite_master`-Probe | `DatabaseInitializer.cs:85-91` | Baseline-/Heal-Pfad ist ein reines SQLite-Legacy-Feature → komplett in den SQLite-Zweig verschieben; Postgres startet IMMER frisch via `MigrateAsync` (es gibt keine Legacy-Postgres-DBs) |

---

## 2. Zielarchitektur

**Entscheidung (bereits getroffen, nicht neu diskutieren):** Multi-Provider via **konfigurierbarem Provider-Switch + getrennten Migrations-Assemblies**, NICHT zwei DbContexts. SQLite bleibt Default (Zero-Config-Versprechen aus `outOfTheBox.md`).

> ⚠️ **Korrektur (2026-07-09, siehe [ADR-0004](../adr/0004-postgres-provider-support.md)):** Das unten skizzierte „zwei Migrations-**Ordner** im selben Projekt" funktioniert **nicht** — EF Core erlaubt nur EINEN `ModelSnapshot` pro DbContext pro Assembly (zwei ⇒ „more than one snapshot"). Zudem erzeugt ein separates Migrations-Projekt, das den `MetricsDbContext` referenziert, einen **Zirkelbezug** mit der App. **Korrigierter Weg:** `MetricsDbContext` + Entities in ein Class-Lib-Projekt **`Whiskers.Data`** extrahieren (Namespace `Whiskers.Services.Persistence` **beibehalten** → minimaler Konsumenten-Churn); zwei **Migrations-Projekte** `Whiskers.Migrations.Sqlite` (bestehende `InitialCreate` unverändert hierher) + `Whiskers.Migrations.Postgres` (neu), je eigener Snapshot, `MigrationsAssembly` je Provider. Schritte 3 und 8 unten sind entsprechend zu lesen. Steps 1–2 (Provider-Switch, DateTime-UTC) sind bereits umgesetzt (Commits `53f442d`, `caa4942`).

```
appsettings.json / ENV:
  Database__Provider = "sqlite" (default) | "postgres"
  Database__ConnectionString = "" (default → SQLite-Pfad wie bisher)
                                bzw. "Host=...;Database=whiskers;Username=...;Password=..."
  ENV-Aliase: WHISKERS_DB_PROVIDER, WHISKERS_DB_CONNECTION
```

```
Program.cs
   └── AddWhiskersDatabase(builder)          // neue Extension in Services/Persistence/
         ├── liest DatabaseOptions (Options-Pattern, validiert)
         ├── provider == sqlite  → UseSqlite(cs, x => x.MigrationsAssembly("Whiskers") /* Ordner Migrations/Sqlite */)
         └── provider == postgres→ UseNpgsql(cs, x => x.MigrationsAssembly("Whiskers") /* Ordner Migrations/Postgres */
                                              .EnableRetryOnFailure(3))
```

- **Zwei Migrations-Ordner im selben Projekt:** `Migrations/Sqlite/` (bestehende `InitialCreate` dorthin verschieben, Namespace anpassen) und `Migrations/Postgres/` (neu scaffolden). EF Core unterstützt das über zwei Design-Time-Factories (`--context`-frei, via `-- --provider`-Argument oder zwei Factory-Klassen).
- **Kein Auto-Cross-Migrate:** Whiskers migriert NICHT automatisch Daten von SQLite nach Postgres beim Providerwechsel. Stattdessen: expliziter CLI-Befehl (Schritt 7).

---

## 3. Implementierungsschritte (in dieser Reihenfolge abarbeiten)

> **Fortschritt (2026-07-09, Branch `feat/stabledb-postgres`):**
> - ✅ **Schritt 1** — `DatabaseOptions` + `AddWhiskersDatabase` (Provider-Switch, Commit `53f442d`).
> - ✅ **Schritt 2** — DateTime-UTC-Härtung via `UtcDateTimeConverter` + `ConfigureConventions` (Commit `caa4942`).
> - ✅ **ADR-0004** — Migrations-Architektur entschieden (separate Assemblies + `Whiskers.Data`, Commit `b13d293`).
> - ✅ **Schritt 3** — Migrations-Split vollständig umgesetzt (statt „zwei Ordner", per ADR-0004):
>   - `Whiskers.Data` extrahiert (Context + 15 Entities + Converter, Namespaces erhalten → null Konsumenten-Churn) — Commit `0f369c3`.
>   - `Whiskers.Migrations.Sqlite` (bestehende `InitialCreate` byte-identisch verschoben, Baseline intakt) — Commit `755f1fa`.
>   - `Whiskers.Migrations.Postgres` (frisch gescaffoldete PG-`InitialCreate`: `bigint` Identity, `timestamptz`, `text`) + provider-verzweigende Design-Time-Factory — Commit `0de6655`.
> - ✅ **Schritt 5** — `DatabaseInitializer` verzweigt auf `IsSqlite()` (SQLite: Legacy-Heal/WAL unverändert; Postgres: nur `MigrateAsync` + `EnableRetryOnFailure`) — Commit `ec6e115`.
> - ✅ **Schritt 4** — Query-Portabilität: einzige echte Roh-SQL-Inkompatibilität war `InAppNotificationStore.MarkAllRead` (`"Read" = 1` gegen PG-`boolean`) → auf `ExecuteUpdate` umgestellt (Commit `c6e3827`). Übrige Roh-SQL (`DELETE FROM`, `LIMIT`-Subquery) sind portabel; alle anderen Stores nutzen LINQ (EF übersetzt provider-korrekt).
> - ✅ **Schritt 6** — Compose/Deploy + Doku (Commit `f05516f`): auskommentierter DB-Block in `docker-compose.yml` (Default bleibt SQLite; leeres `WHISKERS_DB_PROVIDER` wird abgelehnt), Overlay `deploy/docker-compose.postgres.yml` (`postgres:17-alpine` + `pg_isready`-Healthcheck + `depends_on: service_healthy`), README-Konfig-Zeile + `.env.example`-Abschnitt, Secret-File (`_CONNECTION_FILE`) schon in `DatabaseRegistration`. **Plus Per-Folder-READMEs** für `Whiskers.Data` + `Whiskers.Migrations.{Sqlite,Postgres}` + aktualisiertes `Services/Persistence/README`.
> - ✅ **Schritt 7** — CLI-Datenumzug `--migrate-to-postgres "<conn>"` (Commit `e38be0a`): `SqliteToPostgresMigrator` — Ziel migrieren, **Leer-Prüfung** (Abbruch statt Merge), 15 Tabellen streamend in 5000er-Batches ohne Id kopieren (neue Identity, Business-Keys erhalten), Quelle nie verändert, Row-Count-Report. Hook in `Program.cs` (short-circuit, kein Host-Boot). Provider-agnostischer Kern per 2 SQLite-Contexts getestet (Kopie/ID-Neuvergabe/Business-Keys/Quelle-safe + Abbruch bei nicht-leerem Ziel) → **301 Tests grün**; CLI-Exit-Code lokal verifiziert.
> - 🟡 **Schritt 8** — `PostgresSmokeTests` (Testcontainers `postgres:17`, Commit `3008278`): 2 Tests — (a) DatabaseInitializer-PG-Zweig + alle 15 Tabellen + UTC-Round-Trip + `ExecuteDelete`/`ExecuteUpdate` auf echtem PG; (b) `--migrate-to-postgres`-Kopierkern SQLite→echtes PG (Business-Key erhalten, Id neu). `[Trait Category=RequiresDocker]` + `SkippableFact` → `dotnet test --filter Category!=RequiresDocker` bleibt überall grün (301); Smoke-Tests via `--filter Category=RequiresDocker` mit Docker. **Compile-verifiziert + Baseline grün, aber lokal noch nicht gelaufen** (Docker Desktop auf dem Dev-Rechner kam nicht hoch — WSL-Distro/Dienst „Stopped"). PG ist unabhängig davon end-to-end bewiesen (manueller Badwolf-Lauf).
> - ✅ **PG-Laufzeitbeweis** (dein „Docker-Host einbinden"): idempotentes PG-Skript auf einer **ephemeren `postgres:16` auf Badwolf** angewendet (md5-Gate) → **16 Tabellen**, `Id`→`bigint`, `Timestamp`→`timestamp with time zone`, Migration in `__EFMigrationsHistory` registriert. Container + Temp-Dateien restlos entfernt.
> - Verifikation je Increment: Build clean, **299 Tests grün**, kein Snapshot-Drift (beide Provider), Development-Boot-Gate migriert SQLite cross-assembly.
>
> **Offen:** nur noch die `PostgresSmokeTests` **lokal grün laufen lassen** (blockiert durch das kaputte Docker Desktop auf dem Dev-Rechner; laufen in CI oder sobald Docker gesund ist — `dotnet test --filter Category=RequiresDocker`). Alles andere (Steps 1–8) ist committet. Danach DoD-Abgleich (§6) + ggf. Push/Merge (auf Anweisung).

### Schritt 1 — DatabaseOptions + Registrierungs-Extension
1. Neue Datei `src/Whiskers/Configuration/DatabaseOptions.cs`:
   ```csharp
   public sealed class DatabaseOptions
   {
       public const string SectionName = "Database";
       public string Provider { get; set; } = "sqlite";      // "sqlite" | "postgres"
       public string ConnectionString { get; set; } = "";     // leer => Default-SQLite-Pfad
   }
   ```
2. Neue Datei `src/Whiskers/Services/Persistence/DatabaseRegistration.cs` mit `AddWhiskersDatabase(this WebApplicationBuilder builder)`:
   - Default-SQLite-CS: `$"Data Source={dataDir}/metrics.db"` — dabei den bisher hart codierten Pfad `/app/data` über die bereits existierende Daten-Verzeichnis-Logik beziehen (in `Program.cs` nach `"/app/data"` suchen; falls dort mehrfach ein Literal steht, EINEN gemeinsamen `DataPaths`-Helper einführen — der existiert evtl. schon, prüfen!).
   - Unbekannter Provider-Wert → beim Start mit klarer Fehlermeldung abbrechen (fail fast, kein stiller Fallback).
   - `ServiceLifetime.Transient` beibehalten (bewusste Entscheidung, viele kurzlebige Scopes in Background-Loops).
3. In `Program.cs:177-180` den `AddDbContext`-Block durch `builder.AddWhiskersDatabase();` ersetzen.
4. NuGet: `Npgsql.EntityFrameworkCore.PostgreSQL` (Version passend zu EF Core 10.0.x) in `Whiskers.csproj` ergänzen.

### Schritt 2 — DateTime-UTC-Härtung (VOR dem Postgres-Scaffold!)
Npgsql 6+ mappt `DateTime` auf `timestamptz` und wirft bei Kind ≠ Utc. Alle Schreiber nutzen bereits `DateTime.UtcNow`, aber gelesene SQLite-TEXT-Werte kommen als `Unspecified` zurück. Zwei Absicherungen:
1. In `MetricsDbContext.OnModelCreating` global per ConfigurationConvention einen UTC-Converter registrieren:
   ```csharp
   configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
   ```
   `UtcDateTimeConverter`: schreibt `v.ToUniversalTime()`, liest `DateTime.SpecifyKind(v, DateTimeKind.Utc)`. (In `ConfigureConventions` überschreiben, neue Datei `Services/Persistence/UtcDateTimeConverter.cs`.)
2. Unit-Test ergänzen: Roundtrip eines Entities → `Kind == Utc` nach dem Lesen, für beide Provider.

### Schritt 3 — Migrations-Split
> ⚠️ **Ersetzt durch [ADR-0004](../adr/0004-postgres-provider-support.md):** NICHT „Ordner im selben Projekt" (EF-Snapshot-Konflikt + Zirkelbezug), sondern **`Whiskers.Data`-Extraktion + zwei Migrations-Projekte**. Konkret: (a) neues Class-Lib `Whiskers.Data` mit `MetricsDbContext` + Entities + `UtcDateTimeConverter`, Namespace(s) beibehalten. **⚠️ Verfeinerung (2026-07-09):** die 15 Entities sind NICHT beisammen — 4 stehen in `MetricsDbContext.cs`, **11 in `Whiskers.Models`** (verteilt über ~7 Dateien: `AuditLogEntry.cs`, `McpToolCall.cs`, `VolumeBackup.cs`, `ScheduledTask.cs`, `LogAlertRule.cs`, `UpdatePolicy.cs`, `Cve/CveFirstSeenEntity.cs`), teils gemischt mit Nicht-DB-Modellen; `Whiskers.Models` ist zudem app-gekoppelt (`Agent/AgentRuntime.cs`→`Services`). **Erst-Teilschritt vor der Extraktion:** die 11 Entity-Klassen aus `Models` herauslösen (Dependency-Closure prüfen: referenzieren sie Enums/DTOs, die app-gekoppelt sind?), in `Whiskers.Data` verschieben — Namespace pro Entity beibehalten, damit Konsumenten-`using`s (`Whiskers.Models` / `Whiskers.Services.Persistence`) unverändert bleiben. Erst danach (b)/(c)/(d); (b) neues `Whiskers.Migrations.Sqlite` — bestehende `InitialCreate` + Snapshot **unverändert** (Name/Klasse!) hierher, referenziert `Whiskers.Data`; (c) neues `Whiskers.Migrations.Postgres` — PG-`InitialCreate` scaffolden (`dotnet ef ... --project Whiskers.Migrations.Postgres --startup-project Whiskers`), referenziert `Whiskers.Data`; (d) App referenziert alle drei; `UseSqlite`/`UseNpgsql` setzen `MigrationsAssembly("Whiskers.Migrations.Sqlite"|"...Postgres")`. Die folgenden Original-Punkte gelten sinngemäß, aber mit dieser Projektstruktur:
1. Bestehende Migration + Snapshot nach `Migrations/Sqlite/` verschieben, Namespace `Whiskers.Migrations.Sqlite`. **ACHTUNG:** Der Migrations-**Name** (`20260707164258_InitialCreate`) darf sich NICHT ändern, sonst bricht die Baseline-Erkennung aus ADR-0003 (`db.Database.GetMigrations().First()`) und bestehende Deployments re-migrieren. Namespace-Änderung ist unkritisch, Datei-/Klassenname nicht anfassen. Nach dem Verschieben `DbMigrationBaselineTests` laufen lassen — die decken genau das ab.
2. Zwei Design-Time-Factories in `Services/Persistence/`:
   - `SqliteDbContextFactory` (bestehende `MetricsDbContextFactory` umbenennen/anpassen, `MigrationsAssembly`+Namespace-Hinweis via `x => x.MigrationsHistoryTable(...)` NICHT nötig — nur `UseSqlite` + `MigrationsAssembly` reicht nicht für Ordnertrennung: stattdessen `options.UseSqlite(...); ` und beim Scaffolden `dotnet ef migrations add X --output-dir Migrations/Sqlite --namespace Whiskers.Migrations.Sqlite`).
   - `PostgresDbContextFactory : IDesignTimeDbContextFactory<MetricsDbContext>` — liest `WHISKERS_DB_CONNECTION` oder nutzt `Host=localhost;Database=whiskers_design;Username=postgres;Password=postgres`.
   - Da EF Core bei mehreren Factories meckert: Auswahl über ENV `WHISKERS_DB_PROVIDER` in EINER Factory implementieren (eine Factory, die intern verzweigt) — das ist der einfachste stabile Weg.
3. Postgres-Initial-Migration scaffolden:
   ```
   $env:WHISKERS_DB_PROVIDER="postgres"
   dotnet ef migrations add InitialCreate --output-dir Migrations/Postgres --namespace Whiskers.Migrations.Postgres --project src/Whiskers
   ```
4. Generierte Postgres-Migration reviewen: `bigint identity` PKs, `text`, `timestamp with time zone`, `boolean`, `double precision` — alle Indizes/Unique-Constraints aus `MetricsDbContext.cs:83-155` müssen 1:1 auftauchen.

### Schritt 4 — Query-Portabilität
1. `McpCallLogStore.cs:76-77`: `.Contains(...)` → case-insensitiv provider-neutral machen (siehe U1).
2. `InAppNotificationStore.cs:153-155`: Das rohe `DELETE ... NOT IN (... LIMIT {0})` funktioniert in Postgres, ABER sicherer: durch LINQ `ExecuteDeleteAsync` mit Subquery ersetzen oder so belassen und einen Integrationstest gegen beide Provider schreiben. **Entscheidung: belassen + Test** (minimale Diff-Fläche).
3. Repo-weiter Check auf weitere `ExecuteSqlRaw`-Aufrufe (Stand heute: nur die genannten; nach Umbau erneut greppen).

### Schritt 5 — DatabaseInitializer verzweigen
`DatabaseInitializer.InitializeAsync` bekommt Provider-Bewusstsein (über `db.Database.ProviderName` oder injizierte `DatabaseOptions`):
- **SQLite-Zweig (unverändert):** Legacy-Heal (`sqlite_master`-Probe, `LegacyHealSql`, Baseline-Stamping), dann `MigrateAsync`, dann WAL-PRAGMA. Verhalten identisch zu heute — `DbMigrationBaselineTests` müssen grün bleiben.
- **Postgres-Zweig (neu, trivial):** nur `await db.Database.MigrateAsync()`. KEIN Heal-Pfad (es existieren keine Legacy-Postgres-Installationen), KEINE PRAGMAs.
- Fehlerbild verbessern: Wenn Postgres nicht erreichbar → Retry mit Backoff (z. B. 5 Versuche à 3 s; K8s-Szenario: DB-Pod startet parallel), danach fail fast mit klarer Meldung inkl. Host (ohne Passwort zu loggen!).

### Schritt 6 — Compose/Deploy-Integration
1. `docker-compose.yml`: auskommentiertes Beispiel-Postgres-Service-Block + die zwei ENV-Variablen dokumentieren.
2. Neues `deploy/docker-compose.postgres.yml` (Overlay): `postgres:17-alpine`, Volume, Healthcheck `pg_isready`, `depends_on: condition: service_healthy` für Whiskers.
3. README „Configuration“-Abschnitt: Tabelle mit `WHISKERS_DB_PROVIDER` / `WHISKERS_DB_CONNECTION` ergänzen.
4. **Secrets:** Connection-String enthält Passwort → `WHISKERS_DB_CONNECTION` muss auch via Docker-Secret-File unterstützt werden: Konvention `WHISKERS_DB_CONNECTION_FILE=/run/secrets/db_conn` (Datei gewinnt über ENV). Kleine Helper-Funktion in `DatabaseRegistration`.

### Schritt 7 — Datenmigration SQLite → Postgres (einmaliger Umzug)
Neuer CLI-Einstieg (kein UI): `dotnet Whiskers.dll --migrate-to-postgres "<conn>"` bzw. im Container `docker exec whiskers dotnet Whiskers.dll --migrate-to-postgres ...`:
1. Öffnet Quelle (SQLite, Default-Pfad) und Ziel (Postgres) als zwei DbContexts.
2. Ziel: `MigrateAsync`, dann prüfen, dass ALLE 15 Tabellen leer sind (sonst Abbruch — kein Merge).
3. Tabellenweise kopieren in Batches à 5.000 (`AsNoTracking`, Identity-Insert ist nicht nötig, da nur `long Id`-PKs: `Id` mitkopieren via `ALTER TABLE ... ALTER COLUMN` ist unnötig — stattdessen beim Insert die IDs NICHT zurücksetzen; EF: `IDENTITY`-Spalten erfordern `OVERRIDING SYSTEM VALUE` → einfacher: rohe `COPY`-freie Inserts via EF mit `db.Database.OpenConnection()` + `SET session_replication_role` NICHT verwenden. **Pragmatische Lösung:** IDs der Metrik-/Log-Tabellen sind reine Surrogate ohne FK-Referenzen (verifizieren: es gibt KEINE FK-Beziehungen zwischen den 15 Tabellen!) → Zeilen OHNE Id einfügen, neue IDs sind ok. Nur die Unique-Business-Keys (`BackupId`, `TaskId`, `RuleId`, `IdentityKey`) müssen erhalten bleiben — sind normale Spalten, unkritisch.)
4. Danach Sequenzen korrekt (da frisch vergeben), Row-Counts je Tabelle loggen und als Abschlussbericht ausgeben.
5. **DB-Safety-Anforderung des Projekts beachten:** Quelle wird NIE verändert; vor dem Umzug loggt das Tool den Hinweis, `metrics.db` zu sichern; Abbruch bei nicht-leerem Ziel.

### Schritt 8 — Tests & CI
1. `DbMigrationBaselineTests` unverändert grün (SQLite-Pfad).
2. Neue `PostgresSmokeTests` (xUnit-Collection, per Testcontainers-for-.NET `postgres:17-alpine`): Migrate → Insert je Entity → Query → Retention-`ExecuteDeleteAsync` → `McpCallLogStore`-Filter (Case-Insensitivity!). Mit `[Trait("Category","RequiresDocker")]` markieren und in CI nur ausführen, wenn Docker verfügbar (lokal auf Windows-Dev-Maschine ggf. skippen).
3. DI-Boot-Gate (siehe Memory/Projektregel): App einmal mit `Provider=postgres` + Testcontainer im Development-Mode booten (`ValidateOnBuild`), bevor deployt wird.

---

## 4. Wie NICHT (Anti-Patterns, ausdrücklich vermeiden)

- **KEINEN zweiten DbContext** (`PostgresMetricsDbContext`) einführen — doppelte Model-Pflege, Snapshot-Drift.
- **KEIN** `EnsureCreated` für Postgres — ausschließlich Migrationen (ADR-0003-Linie fortführen).
- **KEINE** automatische Datenübernahme beim Providerwechsel im Normalstart — nur explizit per CLI (Schritt 7). Stiller Datenumzug beim Boot verletzt die DB-Safety-Anforderung des Projekts.
- **NICHT** die bestehende `InitialCreate`-Migration umbenennen oder neu generieren (bricht Baseline bestehender Installationen).
- **KEINE** provider-spezifischen `EF.Functions.*`-Aufrufe in Shared-Code ohne Provider-Weiche.
- **NICHT** die JSON-Stores (`servers.json`, `vault.json`, …) „bei der Gelegenheit“ mit in die DB ziehen — das ist ein eigenes Arbeitspaket (siehe `changeme.md` C7), sonst wird dieser PR unreviewbar groß.

## 5. Abhängigkeiten zu anderen Dokumenten

- `kubernetesImplement.md` **hängt hiervon ab**: Multi-Replica/HA setzt Postgres voraus (SQLite-File-Lock verträgt keine zwei Pods). Postgres-Support ist dort Voraussetzung P1.
- `outOfTheBox.md`: Der Setup-Wizard bekommt einen optionalen „Datenbank“-Schritt (Default SQLite, Toggle Postgres) — erst NACH diesem Dokument umsetzbar.
- `changeme.md` C7 (JsonFileStore → DB) baut auf dem hier geschaffenen Provider-Switch auf.

## 6. Definition of Done

- [ ] `WHISKERS_DB_PROVIDER=postgres` + Connection-String → App bootet, migriert, alle Features (Metriken, Notifications, Audit, Scheduler, CVE-Age) funktionieren.
- [ ] Default-Start ohne jede Konfiguration verhält sich byte-identisch zu heute (SQLite, WAL, Baseline-Heal).
- [ ] `DbMigrationBaselineTests` + neue `PostgresSmokeTests` grün.
- [ ] CLI-Umzugsbefehl kopiert eine reale `metrics.db` verlustfrei (Row-Counts identisch, Stichprobenvergleich).
- [ ] README + `docs/adr/`: neues ADR „0004-postgres-provider-support“ (adr-writer-Format), README-Konfig-Tabelle ergänzt.
- [ ] Per-Folder-READMEs der geänderten Ordner aktualisiert (Projektregel!).
