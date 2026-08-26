# Plan-0013: MCP- und Agenten-Oberfläche (Querschnitt)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Dieser Plan hat eine ungewöhnliche Stellung: **WP1 bis WP3 laufen vor allen anderen Paketen.** Sie legen die Form fest, in der Werkzeuge entstehen — danach entsteht jedes Paketwerkzeug direkt richtig. Umgekehrt müssten zwölf Pakete nachträglich umgebaut werden.

WP4 (Nachrüstung der stummen Module) und WP5 (Katalogpflege) laufen begleitend.

Zwei Ist-Befunde geben die Richtung vor: Ein Werkzeug ohne Eintrag in `DefaultToolLevels` fällt auf `admin` zurück und ist für den Agenten damit tot; und der Registrierungstest prüft nur `count > 40`, deckt also den Ausfall eines einzelnen Moduls nicht ab — nachdem genau diese Klasse Fehler den MCP-Server über mehrere Releases hinweg auf null Werkzeuge gesetzt hat.

## Ziele

- Werkzeugstufe entsteht neben dem Werkzeug, nicht in einer Liste nebenan.
- Ein weggefallenes Werkzeug bricht den Build.
- Kein Modul bleibt ohne Oberfläche.

## Arbeitspakete

### WP1: Stufe ans Werkzeug

**Zweck:** Die stille Admin-Sperre unmöglich machen.
**Schätzung:** M (2 Tage). **Vor allen Paketen.**

1. **WP1.1:** Attribut `[McpToolLevel(McpPermissionLevels.Read|Write|Admin)]` neben `[McpServerTool]`.
2. **WP1.2:** `DefaultToolLevels` aus den Attributen erzeugen oder beim Start dagegen prüfen — eine Quelle der Wahrheit, nicht zwei.
3. **WP1.3:** Laufzeitverhalten unverändert lassen: unbekanntes Werkzeug bleibt `admin` (fail-closed). Der Unterschied ist, dass es nie ausgeliefert wird.
4. **WP1.4:** Alle vorhandenen Werkzeuge mit dem Attribut versehen, Stufen 1:1 aus dem heutigen Wörterbuch übernehmen — **keine** Stufenänderung in diesem Schritt, das wäre eine unbemerkte Rechteänderung.

**Abnahme:** Ein Werkzeug ohne Attribut lässt den Testlauf fehlschlagen. Die erzeugte Stufenzuordnung ist mit dem heutigen Wörterbuch bitgleich.

> 🟢 **WP1 erledigt** (2026-08-26). `Mcp/McpToolLevelAttribute.cs` (`[McpToolLevel(...)]`, wirft bei unbekannter Stufe statt zu normalisieren) + `Mcp/McpToolLevelCatalog.cs` (Reflexion über die Werkzeugtypen, PascalCase→snake_case, `Undeclared`-Liste). **Alle 67 Werkzeuge annotiert**, Stufen 1:1 aus `DefaultToolLevels` übernommen — keine einzige geändert.
>
> **Bewusste Abweichung von WP1.2:** `DefaultToolLevels` bleibt der Laufzeitpfad und wurde **nicht** durch Reflexion ersetzt. Grund: eine fehlschlagende Reflexion im RBAC-Pfad wäre zwar fail-closed, würde aber die gesamte MCP-Oberfläche lautlos abschalten — exakt die Fehlerform aus 0.12.0. Das Attribut ist die Deklaration, das Wörterbuch der Vollzug, und `McpToolLevelTests` hält beide in beide Richtungen deckungsgleich. Ein Reflexionsfehler bricht damit einen Test, keine Anfrage.
>
> **Gegenbeweis geführt** (nicht nur „Tests grün"): Attribut an `SearchLogs` testweise entfernt ⇒ 2 Tests rot, mit namentlicher Nennung (`LogTools.SearchLogs -> search_logs` und `search_logs: in the dictionary, but no tool declares it`). Danach wiederhergestellt. Ohne diesen Lauf wäre unbelegt, dass der Wächter zubeißt.
>
> Ergebnis: Build 0 Fehler/keine neuen Warnungen, **627/627 Tests grün** (vorher 621). `Mcp/README.md` und der XML-Kommentar an `DefaultToolLevels` fortgeschrieben. **Offen aus WP1:** nichts. Nächster Schritt ist WP2 (Registrierungstest je Modul).

### WP2: Registrierungstest verschärfen

**Zweck:** Den Ausfall eines einzelnen Moduls sichtbar machen.
**Schätzung:** S (1 Tag).

1. **WP2.1:** `McpToolRegistrationTests` um eine Prüfung **je Modul** erweitern: erwartete Werkzeugzahl je `McpToolTypes`-Eintrag.
2. **WP2.2:** Die bestehende Überladungsfallen-Prüfung bleibt unverändert erhalten.
3. **WP2.3:** Gegenprobe im Test selbst: Entfernt man ein Modul aus der Liste, schlägt der Test fehl.
4. **WP2.4:** **Das dritte Namensvorkommen absichern** (Nachtrag aus WP1). Der Werkzeugname existiert an drei Stellen: Methodenname (daraus leitet das SDK den Wire-Namen ab), String-Literal im `McpPermissionCheck.CheckAccess`-Aufruf, Schlüssel in `DefaultToolLevels`. WP1 hat Methodenname↔Wire-Name und Wire-Name↔Wörterbuch verzurrt — **das Literal nicht**. Ein Tippfehler wie `CheckAccess(..., "list_container")` fällt weiterhin still auf `admin` zurück und macht das Werkzeug für den Agenten unerreichbar, ohne Fehler und ohne Logzeile.
   Ein quelltextlesender Test prüft je Werkzeugmethode, dass das übergebene Literal dem abgeleiteten Namen entspricht. Quelldateien über einen zur Laufzeit auflösbaren Pfad finden (`MSBuild`-Property oder Suche ab dem Testverzeichnis aufwärts); findet der Test keine Quellen, schlägt er fehl, statt still zu bestehen — ein Prüfer, der nichts findet und grün meldet, ist die Fehlerform, gegen die dieses Paket antritt.

**Abnahme:** Ein absichtlich entferntes Modul bricht den Build — heute bliebe der Test grün. Ein absichtlich verfälschtes `CheckAccess`-Literal ebenfalls.

> 🟢 **WP2 erledigt** (2026-08-26). `McpToolRegistrationTests` um `ExpectedToolsPerModule` erweitert (all-in-one 40, scheduler 4, logmonitor 3, cve 4, cloud-control 15, agent 1 = 67): Prüfung je Modul, in beide Richtungen (fehlendes Modul **und** nicht eingetragenes neues Modul), Gesamtzahl exakt statt `> 40`, plus ein Test gegen doppelt beanspruchte Werkzeugtypen — der Fall, der beim Schrumpfen von `AllInOnePseudoModule` entstehen kann. WP2.4 als quelltextlesender Test in `McpToolLevelTests`.
>
> **Befund unterwegs:** Sechs CloudTools-Werkzeuge (`cloud_power_on`, `cloud_shutdown`, `cloud_reboot`, `cloud_hard_reset`, `cloud_create_snapshot`, `cloud_metrics`) rufen `CheckAccess` nicht direkt auf, sondern über einen privaten `Guarded(...)`-Helfer, der den Werkzeugnamen als Parameter nimmt. Die Prüfung findet also statt — ein erster, zu enger Testentwurf hätte sie fälschlich als ungeschützt gemeldet. Deshalb lautet die Zusicherung „die Methode nennt ihren eigenen Werkzeugnamen als Literal" und nicht „sie ruft `CheckAccess` damit auf": ein guter Helfer darf durch einen Test, der nur eine Aufrufform kennt, nicht bestraft werden.
>
> **Gegenbeweise geführt** (beide, dann wiederhergestellt):
> - `Cve.CveModule` aus `ModuleCatalog` entfernt ⇒ 2 Tests rot: `module 'cve' contributes no tools any more (expected 4)` und `Expected: 67 / Actual: 63`. **Der alte `count > 40`-Test wäre bei 63 grün geblieben** — genau der Nachweis, den WP2 verlangt.
> - Literal `"search_logs"` zu `"search_log"` verfälscht ⇒ `LogTools.cs.SearchLogs: never mentions its own tool name "search_logs"`.
>
> Der Quelltext-Scanner zählt zusätzlich mit und vergleicht gegen die Zahl der Deklarationen: ein Parser, der nichts mehr findet und sauber meldet, wäre dieselbe Fehlerform, gegen die das Paket antritt.
>
> Ergebnis: Build 0 Fehler, **630/630 Tests grün** (vorher 627). **Offen aus WP2:** nichts. Nächster Schritt ist WP3 (Katalog-Momentaufnahme).

### WP3: Katalog-Momentaufnahme

**Zweck:** Abweichungen bewusst machen.
**Schätzung:** S (1 Tag).

1. **WP3.1:** Erzeugte Datei `docs/mcp-tool-catalog.md` bzw. eine Snapshot-Datei: Werkzeugname, Stufe, Modul, Kurzbeschreibung.
2. **WP3.2:** Testlauf vergleicht den erzeugten Katalog mit der eingecheckten Fassung; Abweichung bricht, bis sie übernommen wird.
3. **WP3.3:** Ende-zu-Ende-Prüfung gegen einen **laufenden** Server: `tools/list` gegen den Katalog. Der Unterschied zwischen „registriert im Test" und „ausgeliefert im Betrieb" ist genau die Lücke, die die Überladungsfalle so lange offen ließ.

**Abnahme:** Katalog im Repo, Testvergleich aktiv, Ende-zu-Ende-Zählung dokumentiert.

> 🟢 **WP3 erledigt** (2026-08-26). `Mcp/McpToolCatalogRenderer.cs` erzeugt [`docs/mcp-tool-catalog.md`](../mcp-tool-catalog.md) (Werkzeug, Stufe, Modul, Beschreibung, nach Modul gruppiert, deterministisch ohne Zeitstempel): **67 Werkzeuge — 33 read, 31 write, 3 admin** (`deploy_app`, `deploy_compose`, `execute_command`). `McpToolCatalogSnapshotTests` vergleicht gegen den eingecheckten Stand und schreibt bei Abweichung eine `.actual`-Fassung daneben, **liest sie aber nie zurück** — ein Schnappschuss, der sich selbst repariert, pinnt nichts. Dazu zwei Ergänzungen: kein Werkzeug ohne Beschreibung (der Agent wählt danach aus) und kein Werkzeug von zwei Modulen beansprucht.
>
> **WP3.3 als echter Handshake:** `McpServedSurfaceTests` bootet die reale Anwendung über `WebApplicationFactory<Program>` (Muster aus `BootMatrixTests`, `Auth:Disabled` liefert den authentifizierten Principal), führt `initialize` → `notifications/initialized` → `tools/list` über `/mcp` und vergleicht die **geantwortete** Liste mit dem Katalog. Antwort kann JSON oder SSE sein, beides wird ausgewertet.
>
> **Der Gegenbeweis, auf den es ankam:** Den 0.12.0-Fehler im echten Startpfad nachgebaut (`IEnumerable<Type>` → `Type[]` in `WhiskersHostingExtensions`). Ergebnis: **genau ein Test fällt** — `McpServedSurfaceTests`, mit dem historischen Symptom `-32601 Method 'tools/list' is not available.` Die anderen sieben MCP-Tests bleiben grün, weil sie Code betrachten und nicht den laufenden Server. Damit ist empirisch belegt, was dieses Paket behauptet: **keine der code-prüfenden Zusicherungen hätte 0.12.0 gefunden.** Anschließend zurückgebaut.
>
> Zweiter Gegenbeweis: Stufe von `list_log_alerts` read→write geändert ⇒ Katalogtest und Wörterbuchtest rot. Zurückgebaut.
>
> Ergebnis: **634/634 Tests grün** (vorher 630). `Mcp/README.md` um eine Tabelle „welcher Test beantwortet welche Frage" ergänzt, `docs/README.md` um den Katalog. **Offen aus WP3:** nichts. Nächster Schritt ist WP4 (stumme Module nachrüsten).

### WP4: Stumme Module nachrüsten

**Zweck:** Die bestehenden Lücken schließen.
**Schätzung:** L (4–5 Tage), paketweise schneidbar.

> ⚠️ **Prämisse korrigiert** (2026-08-26, vor Umsetzungsbeginn geprüft). „Sieben stumme Module" stimmt als Zählung, aber nicht als Lückenanalyse. `Array.Empty<Type>()` heißt nur, dass das Modul selbst keine Werkzeugtypen beisteuert — nicht, dass die Fähigkeit unerreichbar ist. Tatsächlicher Stand:
>
> | Modul | Dienste | Über MCP erreichbar? |
> |---|---|---|
> | `host-management` | Firewall/Nginx/SSL/Systemd | **ja** — `ServerTools` nutzt alle vier, liegt aber in `all-in-one` |
> | `image-updates` | (keine eigenen) | **ja** — `get_update_status`, `update_container` in `ContainerTools` |
> | `deployment` | `IDeploymentService`, `ITemplateService` | **teilweise** — `deploy_app`/`deploy_compose` gehen über `IDockerService`, die Modul-Dienste selbst sind unerreichbar |
> | `gitdeploy` | `IGitDeployService` | **nein** — echte Lücke |
> | `volumebackups` | `IVolumeBackupService` | **nein** — echte Lücke |
> | `notifications` | — | **nein** — echte Lücke |
> | `terminal` | — | bewusst ausgeschlossen (WP4.2) |
>
> **Echter WP4-Umfang: drei Module, nicht sieben** — `gitdeploy`, `volumebackups`, `notifications`, plus die Frage nach den Modul-Diensten von `deployment`.
>
> **Befund nebenbei, der eine Entscheidung braucht:** `WhiskersHostingExtensions.cs:113` sagt zu, dass „a disabled module's tools drop off the MCP surface automatically". Für `host-management` gilt das **nicht**: seine Werkzeuge liegen in `AllInOnePseudoModule`, und das hat kein Feature-Flag. Wer `Features:host-management:Enabled=false` setzt, verliert die Navigation, behält aber `list_firewall_rules`, `add_firewall_rule`, `update_nginx_config`, `manage_systemd_service` und `renew_ssl_certificate` auf der MCP-Oberfläche. Dasselbe gilt sinngemäß für `deployment` und `image-updates`. Das ist ein bekannter Zwischenzustand der RoadToSAP-Extraktion, aber als Sicherheitszusage falsch. `ServerTools` nach `host-management` zu verschieben würde es beheben und dabei das Verhalten ändern (Abschalten entfernt dann wirklich Werkzeuge) — **Nutzerentscheidung, nicht nebenbei erledigt.** Der neue Katalog macht die Zuordnung erstmals sichtbar.

1. **WP4.1:** Lesende Abdeckung zuerst für die drei echten Lücken: `GitDeployModule`, `VolumeBackupsModule`, `NotificationsModule`. Für `HostManagementModule`, `ImageUpdateModule` und `DeploymentModule` ist keine Nachrüstung nötig — dort ist die Frage die Zuordnung, siehe Befund oben.
2. **WP4.2:** Schreibende Werkzeuge nur, wo der Vorgang für einen Agenten sinnvoll und beherrschbar ist — Volume-Sicherung anstoßen ja, Terminal-Sitzung öffnen nein.
3. **WP4.3:** Beschreibungen nach FR-08: Zweck, Wirkung, Nebenwirkung in einem Satz.
4. **WP4.4:** Je Modul die Katalog-Momentaufnahme mit fortschreiben.

**Abnahme:** Kein Modul liefert unbeabsichtigt `Array.Empty<Type>()`; verbleibende leere Module tragen eine Begründung im Code.

> 🟢 **WP4 erledigt** (2026-08-26), im korrigierten Umfang: drei Module, vier lesende Werkzeuge, **71 statt 67**.
>
> | Modul | Werkzeug | Beantwortet |
> |---|---|---|
> | `gitdeploy` | `list_git_deploy_apps` | „Welche Apps laufen aus Git, und hat der letzte Deploy geklappt?" |
> | `volumebackups` | `list_volume_backups`, `list_volumes` | „Wann wurde dieses Volume zuletzt gesichert?" (mit Alter, nicht nur Zeitstempel) |
> | `notifications` | `list_recent_alerts` | „Was war zuletzt los?" — die Alarme, die Whiskers **selbst schon gezogen hat** |
>
> Das letzte ist die auffälligste Lücke gewesen: Der Agent konnte Containerzustände und Rohlogs lesen, aber nicht die Schlüsse, die das System längst gezogen hatte — er hat sie neu hergeleitet oder übersehen.
>
> **Bewusst NICHT gebaut** (WP4.2): Deploy auslösen (gehört zu GAP-3, zusammen mit Gesundheitsprüfung und automatischem Rücksprung, die ihn erst verantwortbar machen), Volume sichern/zurückspielen (ein Restore überschreibt Livedaten — die Projektregel verlangt belegte Datensicherheit vor jeder Automatisierung), Benachrichtigungen senden (ein Agent, der Kanäle fluten kann, entwertet das einzige Signal, das verlässlich bleiben muss), Terminal-Sitzung. `list_volumes` ist absichtlich dabei: erst der Abgleich Volumes ↔ Backups macht eine Sicherungslücke sichtbar.
>
> **Die Wächter aus WP1–WP3 haben sich dabei live bewährt:** Nach dem Hinzufügen fiel **genau ein** Test — der Katalogtest mit „surface changed". Alle anderen blieben grün, weil Attribut, Wörterbuch, Kategorien, Modulzuordnung und der laufende Server bereits stimmten. Der Katalog war damit der eine bewusste Bestätigungsschritt, genau wie entworfen. Ein bestehender Modultest, der „keine Werkzeuge" festschrieb (`VolumeBackupsModuleTests`), wurde auf die etablierte Konvention der Module *mit* Werkzeugen umgestellt; zwei überholte „no MCP tools"-Kommentare mitgezogen.
>
> Ergebnis: Build 0 Fehler, **634/634 Tests grün**. `Mcp/Tools/README.md` fortgeschrieben. **Offen:** die Zuordnungsfrage aus dem Prämissen-Befund oben (`ServerTools` nach `host-management` verschieben) — Nutzerentscheidung. Nächster Schritt ist WP5.

### WP5: Werkzeugpflicht in den Paketen verankern

**Zweck:** Dass es nicht wieder vergessen wird.
**Schätzung:** S (0,5 Tage, begleitend).

1. **WP5.1:** Jedes Paket-PRD trägt eine `FR-MCP`-Zeile, jeder Plan ein MCP-Arbeitspaket und eine DoD-Zeile. (Bereits eingetragen.)
2. **WP5.2:** Katalogabgleich in die Freigabe-Prüfliste: Ein Release, das Werkzeuge hinzufügt, nennt sie im CHANGELOG **samt Hinweis auf das erneute Verbinden des Konnektors**.
3. **WP5.3:** Kennzahlen aus dem `McpCallLog` regelmäßig ansehen: Ablehnungsquote je Werkzeug, nie aufgerufene Werkzeuge.

**Abnahme:** Ein Release mit neuen Werkzeugen enthält den Katalogeintrag und den Verbindungshinweis.

> 🟢 **WP5 erledigt** (2026-08-26). WP5.1 war mit den `FR-MCP`- und `WP-MCP`-Einträgen bereits erledigt. WP5.2: `CONTRIBUTING.md` bekommt unter „Before you open a pull request" einen MCP-Punkt (Attribut, Wörterbucheintrag, `McpToolTypes`, Katalog auffrischen — plus die Pflicht, im CHANGELOG auf das nötige **Neuverbinden** hinzuweisen, weil Konnektoren die Werkzeugliste nur beim Sitzungsstart lesen und ein neues Werkzeug sonst als kaputt gemeldet wird). CHANGELOG unter „Unreleased" um zwei Added- und zwei Fixed-Einträge ergänzt.
>
> WP5.3: **kein neues Dashboard gebaut** — stattdessen die zwei Kennzahlen als fertige SQL-Abfragen in `Mcp/README.md` dokumentiert (Ablehnungsquote je Werkzeug aus `Verdict`, nie aufgerufene Werkzeuge im Abgleich mit dem Katalog), mit dem ausdrücklichen Hinweis, dass es eine periodische Handprüfung ist. Eine eigene Ansicht wäre über den Zuschnitt dieses Pakets hinausgegangen; sie als „erledigt" zu buchen, ohne sie zu bauen, wäre genau die Sorte stiller Lücke, gegen die das Paket antritt.
>
> Ergebnis: Build 0 Fehler, **634/634 Tests grün**.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2 ──> WP3          (vor allen anderen Paketen)
              └──> WP4       (begleitend, paketweise)
WP5 begleitend ab WP1
```

- **Blockiert:** nichts formal — aber jedes Paket, das vor WP1 Werkzeuge liefert, muss danach umgebaut werden.
- **Liefert an:** SP-6 (Schreibwerkzeuge als geprüfte Aktionen), GAP-5 (veröffentlichter Katalog).

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Werkzeugzahl am laufenden Server | `tools/list` gegen Katalog | deckungsgleich |
| Ablehnungsquote je Werkzeug | `McpCallLog` | keine Quote nahe 100 % |
| Nie aufgerufene Werkzeuge | `McpCallLog`, 90 Tage | wenige |
| Module ohne Werkzeuge | Katalog | nur begründete |
| Verkettung Aufruf → Audit → Wirkung | Stichprobe über `CorrelationId` | lückenlos |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Stufenmigration ändert unbemerkt Rechte | Werkzeug wird zu leicht oder gar nicht erreichbar | WP1.4 verlangt bitgleiche Übernahme; Stufenänderungen nur als eigener, begründeter Schritt |
| Test grün, Betrieb stumm | genau die Vorgeschichte dieser Schicht | WP3.3 Ende-zu-Ende gegen den laufenden Server |
| Katalog wuchert | Agent wählt schlechter | Kennzahl nie aufgerufener Werkzeuge; ein Werkzeug je Absicht |
| Schreibwerkzeug ohne Wirkungskontrolle | Automatik ohne Regelkreis | FR-09 verknüpft Schreibwerkzeuge mit SP-6 |
| Nutzer meldet „Werkzeug fehlt" | Fehlersuche am falschen Ende | Verbindungshinweis in CHANGELOG und Freigabemitteilung (WP5.2) |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 | Werkzeug ohne Attribut bricht den Testlauf; Stufenzuordnung bitgleich |
| M2 | WP2 | entferntes Modul bricht den Build |
| M3 | WP3 | Katalog im Repo; `tools/list` am laufenden Server deckungsgleich |
| M4 | WP4 | kein unbeabsichtigt stummes Modul |
| M5 | WP5 | Release mit neuem Werkzeug trägt Katalogeintrag und Verbindungshinweis |

## Rückweg

WP1 bis WP3 sind additiv (Attribut, Test, Datei) und ändern kein Laufzeitverhalten — der Rückweg ist ein Revert ohne Folgen für den Betrieb. WP4 fügt Werkzeuge hinzu; einzelne lassen sich über die Modul-Abschaltung wieder entfernen.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [x] Werkzeug ohne ausdrückliche Stufe bricht den Testlauf — *WP1, Gegenbeweis geführt*
- [x] Entfernen eines Moduls bricht den Build (heute nicht der Fall) — *WP2, Gegenbeweis geführt*
- [x] Verfälschtes `CheckAccess`-Literal bricht den Build (WP2.4) — *Gegenbeweis geführt*
- [x] Katalog-Momentaufnahme im Repo, Testvergleich aktiv — *WP3, Gegenbeweis geführt*
- [x] `tools/list` am **laufenden** Server deckungsgleich mit dem Katalog — *WP3.3; 0.12.0-Fehler nachgebaut, nur dieser Test fiel*
- [x] Stufenzuordnung nach der Migration bitgleich zum heutigen Wörterbuch — *WP1*
- [x] Kein Modul unbeabsichtigt ohne Werkzeuge; verbleibende leere Module begründet — *WP4, Umfang vorher korrigiert*
- [x] CHANGELOG-Eintrag mit Verbindungshinweis für Releases mit neuen Werkzeugen — *WP5, plus Punkt in `CONTRIBUTING.md`*

**Plan-0013 abgeschlossen** (2026-08-26): WP1–WP5 erledigt, 634/634 Tests grün, nichts committet.
Offen und ausdrücklich als Nutzerentscheidung stehengelassen:

1. **Modulzuordnung der Werkzeuge** — `Features:host-management:Enabled=false` entfernt heute die Navigation, aber nicht `add_firewall_rule`, `update_nginx_config`, `manage_systemd_service`, `renew_ssl_certificate`; sie liegen in `AllInOnePseudoModule`, das kein Feature-Flag hat. Die Zusage in `WhiskersHostingExtensions.cs:113` stimmt für diese Werkzeuge nicht. Behebung = `ServerTools` nach `host-management` verschieben, was das Verhalten ändert. Gilt sinngemäß auch für `deployment` und `image-updates`.
2. **Schreibende Werkzeuge** für Deploy, Volume-Backup und Benachrichtigungen — bewusst nicht gebaut (WP4.2), gehören zu GAP-3 bzw. brauchen die Wirkungskontrolle aus SP-6.
3. **Eine Ansicht für die MCP-Kennzahlen** statt der dokumentierten Handabfrage.
