# PRD-0013: MCP- und Agenten-Oberfläche (Querschnitt)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Nutzer des Agenten, Betreiber, die Whiskers per Chat bedienen; alle 12 Pakete dieser Roadmap
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — Querschnitt MCP
- **Ersetzt:** —

## Problem / Motivation

Die Positionierung von Whiskers ist **regierte Autonomie**: der Agent lebt im Produkt, nicht als Adapter davor (siehe [PRD-0012](0012-reife-und-vertrauen.md)). Diese Aussage hält nur, solange die MCP-Oberfläche mitwächst. Jedes Paket, das neue Signale erzeugt oder neue Eingriffe ermöglicht, ohne sie über MCP verfügbar zu machen, macht den Agenten für genau den Bereich blind, den es gerade gebaut hat — und schwächt die Positionierung mit jedem Schritt.

Der Ist-Stand zeigt, dass das kein theoretisches Risiko ist:

**Sieben Module liefern heute null Werkzeuge.** `GitDeployModule`, `DeploymentModule`, `HostManagementModule`, `ImageUpdateModule`, `VolumeBackupsModule`, `NotificationsModule` und `TerminalModule` geben alle `Array.Empty<Type>()` zurück. Der Agent kann also weder deployen noch Images aktualisieren, weder Volume-Sicherungen anstoßen noch Host-Verwaltung betreiben — obwohl die Dienste dahinter existieren und in der Oberfläche bedienbar sind.

**Ein neues Werkzeug ohne Eintrag in `DefaultToolLevels` wird stillschweigend admin-only.** `McpPermissionCheck.cs:31` liest `GetValueOrDefault(toolName, McpPermissionLevels.Admin)`. Ein Werkzeug, das jemand hinzufügt und in dieses handgepflegte Wörterbuch einzutragen vergisst, ist registriert, erscheint in `tools/list` — und wird für den Agenten (Standardobergrenze `write` laut `AiTrigger.MaxLevel`) **immer** abgelehnt. Es sieht vorhanden aus und ist es nie.

**Der vorhandene Registrierungstest prüft eine Untergrenze.** `McpToolRegistrationTests` sichert die `WithTools`-Überladungsfalle ab (`Type[]` registriert null Werkzeuge) und verlangt `count > 40`. Fällt ein einzelnes Modul aus der Liste, bleibt die Zahl über 40 und der Test grün. Genau diese Klasse von Fehler — vorhanden, aber wirkungslos, ohne Fehlermeldung — hat in 0.12.0 bis 0.13.0 dazu geführt, dass der ausgelieferte MCP-Server **null** Werkzeuge bediente, über mehrere Releases hinweg, ohne dass es jemandem auffiel.

Das ist dasselbe Muster wie im Vorfall vom 26.08.: gemessen, geliefert, registriert — und nie bewertet.

## Ziele

- Jedes Paket dieser Roadmap liefert seine MCP-Oberfläche mit; das ist Teil der Definition of Done, nicht ein Nachzügler.
- Ein Werkzeug ohne ausdrückliche Berechtigungsstufe kann nicht ausgeliefert werden.
- Ein weggefallenes Werkzeug bricht den Build, statt still zu verschwinden.
- Die bestehenden Lücken (sieben Module ohne Werkzeuge) werden geschlossen.

## Non-Goals

- **Keine** Aufweichung der Guardrails. Mehr Werkzeuge heißt mehr Regierung, nicht weniger.
- **Keine** automatische Werkzeug-Erzeugung aus Interfaces. Werkzeugzuschnitt ist eine Entwurfsentscheidung — ein Werkzeug je Absicht, nicht je Methode.
- **Kein** eigener MCP-Server neben dem vorhandenen.
- **Keine** Werkzeuge für Vorgänge, die ausdrücklich Menschen vorbehalten sind (Entsperrung nach Schutzabschaltung, Zwei-Personen-Freigaben).

## Zielgruppen / Personas

### Agenten-Nutzer

- Kontext: bedient Whiskers per Chat, erwartet, dass der Agent sieht, was die Oberfläche zeigt.
- Pain Point: Der Agent kann Zustände nicht abfragen, die im Dashboard sichtbar sind — und rät stattdessen.

### Betreiber im Störungsfall

- Pain Point: Will den Agenten fragen „warum ist der Server langsam?" und bekommt eine Antwort ohne Zugriff auf Budget, Circuit-Zustand oder Loop-Gesundheit.

### Whiskers-Entwickler

- Pain Point: Es gibt keine Stelle, an der das Vergessen der MCP-Oberfläche auffällt.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | **Werkzeugpflicht je Paket:** Jedes Paket dieser Roadmap deklariert seine Lese- und Schreibwerkzeuge im PRD und liefert sie im selben Arbeitsstrang aus. | Must |
| FR-02 | Die Berechtigungsstufe wird **am Werkzeug** deklariert (Attribut oder Modul-Metadaten), nicht in einem separaten handgepflegten Wörterbuch. `DefaultToolLevels` wird daraus erzeugt oder dagegen geprüft. | Must |
| FR-03 | Ein registriertes Werkzeug **ohne** ausdrückliche Stufe lässt den Testlauf fehlschlagen — der Rückfall auf `admin` bleibt als Laufzeitverhalten bestehen, darf aber nie ausgeliefert werden. | Must |
| FR-04 | Der Registrierungstest prüft **je Modul** die erwartete Werkzeugzahl, nicht nur eine Gesamtuntergrenze. | Must |
| FR-05 | Eine versionierte Momentaufnahme des Werkzeugkatalogs (Name, Stufe, Modul) liegt im Repo; Abweichungen brechen den Build und müssen bewusst übernommen werden. | Must |
| FR-06 | Die sieben Module ohne Werkzeuge bekommen eine Oberfläche — mindestens lesend: GitDeploy, Deployment, HostManagement, ImageUpdate, VolumeBackups, Notifications, Terminal. | Must |
| FR-07 | Werkzeuge, die auf einem Backend nicht anwendbar sind, antworten mit einer klaren Begründung (`WorkloadCapabilities`), statt zu scheitern oder Leeres zu liefern. | Must |
| FR-08 | Jede Werkzeugbeschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz — der Agent wählt danach aus. | Must |
| FR-09 | Jeder Werkzeugaufruf trägt die `CorrelationId` (WP-05) und erscheint im Audit-Log; Schreibwerkzeuge zusätzlich in der Wirkungskontrolle ([PRD-0006](0006-wirkungskontrolle.md)). | Must |
| FR-10 | Der Werkzeugkatalog inklusive Stufen ist dokumentiert und veröffentlicht — Portainer veröffentlicht seinen ebenfalls. | Should |

## Nicht-Funktionale Anforderungen

- **Sichtbarkeit folgt den Modulen:** Werkzeuge eines abgeschalteten Moduls bleiben unsichtbar. Der vorhandene `IModuleRegistry`-Pfad bleibt die einzige Quelle.
- **Stufenwahl konservativ:** Im Zweifel die höhere Stufe. Ein Werkzeug, das einen Zustand verändert, ist `write`, auch wenn die Veränderung klein ist.
- **Katalogwachstum ist kein Selbstzweck:** Ein Werkzeug je Absicht. Zwanzig feingliedrige Werkzeuge sind für den Agenten schlechter als fünf klare.
- **Verbindungshinweis:** Der Claude-Code-Konnektor liest den Werkzeugkatalog beim Sitzungsstart. Neue Werkzeuge erscheinen erst nach erneutem Verbinden — das gehört in die Freigabemitteilung, sonst gilt das Werkzeug als kaputt.

## User Stories

- **US-01:** Als Betreiber möchte ich den Agenten fragen „warum ist burgcloud langsam?" und erwarte, dass er Budget, Circuit-Zustand und Loop-Gesundheit selbst abruft.
- **US-02:** Als Betreiber möchte ich den Agenten bitten, einen Server in Ruhe zu lassen, ohne die Oberfläche zu öffnen.
- **US-03:** Als Entwickler möchte ich, dass ein vergessenes Werkzeug oder eine vergessene Stufe den Build bricht.

### Flow für US-01

```
Given ein Server mit hoher Last
When der Betreiber den Agenten fragt
Then ruft er get_whiskers_self_status und get_server_budget ab,
     sieht offene Circuits und überalterte Loops,
     und nennt Whiskers selbst als möglichen Verursacher — statt zu raten
```

## Akzeptanzkriterien

- FR-01 bis FR-09 umgesetzt.
- Gegenprobe für FR-03: Ein absichtlich ohne Stufe hinzugefügtes Werkzeug lässt den Testlauf fehlschlagen.
- Gegenprobe für FR-04/FR-05: Das Entfernen eines einzelnen Moduls aus der Werkzeugliste bricht den Build — heute bliebe der Test grün.
- Die sieben Module ohne Werkzeuge haben mindestens lesende Abdeckung.
- Ein Ende-zu-Ende-Lauf gegen den ausgelieferten MCP-Server zählt die Werkzeuge und vergleicht sie mit der Momentaufnahme.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Werkzeugzahl am laufenden Server | `tools/list` gegen die Momentaufnahme | deckungsgleich | Abweichung ⇒ ein Modul ist stumm ausgefallen |
| Werkzeuge ohne Stufe | Testlauf FR-03 | keine | vorhanden ⇒ stille Admin-Sperre für den Agenten |
| Abgelehnte Agentenaufrufe je Werkzeug | Audit-Log / `McpCallLog` | selten und begründet | dauerhaft für ein Werkzeug ⇒ Stufe falsch gesetzt |
| Nie aufgerufene Werkzeuge | `McpCallLog` über 90 Tage | wenige | viele ⇒ Katalog ist aufgebläht oder die Beschreibungen taugen nicht |
| Module ohne Werkzeuge | Katalog-Momentaufnahme | keine unbeabsichtigten | neue leere Module ⇒ FR-01 wird nicht gelebt |
| Verkettung | Stichprobe: Werkzeugaufruf → Audit → Wirkung | lückenlos | Bruch ⇒ FR-09 nicht durchgezogen |

## Woran ich sehe, dass es bricht

1. **Das Werkzeug ist da und wird trotzdem immer abgelehnt.** Der teuerste stille Fehler dieser Schicht, und er existiert heute: fehlender Eintrag in `DefaultToolLevels` ⇒ Rückfall auf `admin` ⇒ der Agent mit `write`-Obergrenze kommt nie durch. In `tools/list` sieht alles vollständig aus. **Messstelle:** Anteil abgelehnter Aufrufe je Werkzeug im `McpCallLog`. Ein Werkzeug mit 100 % Ablehnungsquote ist nicht streng abgesichert, sondern falsch eingestuft.
2. **Der Test, der eine Untergrenze prüft, verdeckt den Ausfall.** `count > 40` bleibt grün, während ein ganzes Modul aus dem Katalog fällt. Die Vorgeschichte ist eindeutig: von 0.12.0 bis 0.13.0 bediente der ausgelieferte MCP-Server null Werkzeuge, über Releases hinweg unbemerkt. **Gegenprobe:** Ein Modul aus der Liste nehmen und prüfen, dass der Build bricht. Tut er es nicht, ist FR-04 nicht erfüllt, egal was der Testbericht sagt.
3. **Der Katalog wächst, die Nutzbarkeit sinkt.** Zwanzig feingliedrige Werkzeuge mit unklaren Beschreibungen machen den Agenten schlechter, nicht besser — er wählt falsch oder gar nicht. **Messstelle:** Anteil nie aufgerufener Werkzeuge über 90 Tage, und die Fehlgriffquote (Aufruf, der sofort von einem anderen Werkzeug gefolgt wird).
4. **Der Agent sieht weniger als das Dashboard.** Das ist der Zustand, den dieses PRD beendet, und der Rückfall ist schleichend: ein Paket liefert, das Werkzeug kommt „später". **Messstelle:** je Paket eine Zeile in der DoD — ohne Werkzeug gilt das Paket als nicht fertig.
5. **Alles richtig gebaut, der Nutzer sieht es trotzdem nicht.** Der Konnektor liest den Katalog beim Sitzungsstart. Ohne erneutes Verbinden fehlt das neue Werkzeug — und wird als Fehler gemeldet. **Gegenmaßnahme:** Der Hinweis gehört in Freigabemitteilung und CHANGELOG, nicht in den Fehlerbericht.

## Do's

- **Stufe am Werkzeug deklarieren**, nicht in einer Liste nebenan. Die Liste vergisst man.
- **Ein Werkzeug je Absicht**, mit einer Beschreibung, die Wirkung und Nebenwirkung nennt.
- **Lesewerkzeuge zuerst.** Diagnose ist der häufigste Agentenfall und risikofrei.
- **Katalog-Momentaufnahme im Repo führen** — Abweichung ist eine bewusste Entscheidung, kein Zufall.
- **Im Zweifel die höhere Stufe.**

## Don'ts

- **Nicht** die Guardrails lockern, um ein Werkzeug nutzbar zu machen. Wenn die Stufe stört, ist die Stufe falsch — oder der Vorgang gehört einem Menschen.
- **Keine** Werkzeuge für Zwei-Personen-Freigaben und Entsperrungen nach Schutzabschaltung.
- **Nicht** `Type[]` an `WithTools` übergeben. Die Falle ist getestet, der Test bleibt.
- **Nicht** ein Werkzeug je Servicemethode erzeugen.
- **Nicht** annehmen, ein Werkzeug sei kaputt, weil der Konnektor es nicht zeigt — erst neu verbinden.

## Abhängigkeiten

- **Querschnitt:** berührt alle Pakete SP-1 bis GAP-5 sowie die AR-Pakete in [attackResponse.md](../roadmap/attackResponse.md).
- **Wird blockiert von:** nichts. FR-02 bis FR-05 sind sofort umsetzbar und sollten **vor** den Paketen kommen, damit deren Werkzeuge direkt in der richtigen Form entstehen.
- **Liefert an:** [PRD-0006](0006-wirkungskontrolle.md) (Schreibwerkzeuge als geprüfte Aktionen), [PRD-0012](0012-reife-und-vertrauen.md) (veröffentlichter Katalog als Beleg der Positionierung).

## Werkzeuge je Paket (Sollzustand)

| Paket | Lesend | Schreibend |
|---|---|---|
| SP-1 | `get_server_budget` (Budget, in-flight, Circuit-Zustand) | — |
| SP-2 | `get_log_scan_status` (Aussperrungen, letzter Erfolg) | `resume_log_scan` |
| SP-3 | `get_whiskers_self_status` (Loops, Latenzen, letzter Erfolg) | — |
| SP-4 | `list_active_alerts`, `get_alert_rules` | `set_host_threshold` (admin) |
| SP-5 | `get_suspension_status` | `suspend_server_loops`, `resume_server_loops` |
| SP-6 | `get_action_outcomes` (Wirkung eigener Aktionen) | — |
| SP-7 | `get_log_hygiene_report` | — (Neuerzeugen bleibt freigabepflichtig) |
| GAP-1 | Seam-Operationen lesend | `scale_workload`, `rollout_restart` |
| GAP-2 | `list_checks`, `get_check_status` | `run_check_now` |
| GAP-3 | `list_deployments`, `get_deploy_log` | `trigger_deploy`, `rollback_deploy` |
| GAP-4 | `get_cluster_role` (Leader je Instanz) | — |
| GAP-5 | — (liefert die Katalogdokumentation) | — |
| AR-1 ff. | `list_incidents`, `get_incident` | `acknowledge_incident`, `set_blocklist` — **nicht** `unlock` |

## Offene Fragen

- **F-01:** Attribut am Werkzeug oder Metadaten am Modul für FR-02? Vorschlag: Attribut, weil es neben der Methode steht und beim Kopieren mitwandert.
- **F-02:** Soll `set_host_threshold` (SP-4) überhaupt existieren? Eine Regeländerung durch den Agenten ist ein Eingriff in die eigene Beobachtung. Vorschlag: nur lesend in v1.
