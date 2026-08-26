# Plan-0003: Selbstbeobachtung (SP-3)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0003](../prd/0003-selbstbeobachtung.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Whiskers exportiert die Container-Inventur der ganzen Flotte und kein einziges Datum über sich selbst. Plan-0001 und Plan-0002 erzeugen bereits die Zähler; dieser Plan macht sie sichtbar, dauerhaft und auswertbar — und legt die eigene Aktionshistorie über die Metrikkurven.

Die wichtigste Einzelkennzahl ist nicht ein Fehlerzähler, sondern **das Alter des letzten erfolgreichen Durchlaufs je Loop und Server**. Fehler werden gezählt, wenn etwas passiert; ein stehender Loop erzeugt gar nichts.

## Ziele

- Sieben Kernzeitreihen über Whiskers selbst, extern abgreifbar und intern gespeichert.
- Eine Ansicht, die eigene Aktionen zeitlich mit Serverlast in Beziehung setzt.
- Nachweisbar vernachlässigbare Eigenkosten.

## Arbeitspakete

### WP1: `ISelfMetrics`

**Zweck:** Ein Ort für alle Selbstkennzahlen.
**Schätzung:** S (1 Tag).

1. **WP1.1:** `Services/Observability/SelfMetrics/` — Zähler, Gauges, Histogramme in-Prozess, thread-sicher, ohne externe Abhängigkeit.
2. **WP1.2:** Label-Satz **fest verdrahtet** auf `server`, `loop`, `operation`, `result`. Container-Bezeichner sind technisch nicht möglich, nicht nur unerwünscht — das verhindert die Kardinalitätsexplosion an der Quelle.
3. **WP1.3:** Zähler aus Plan-0001 WP5 und Plan-0002 WP2 hier anmelden statt doppelt zu messen.

**Ergebnis:** Eine Schnittstelle, die alle Loops bedienen.

> 🟢 **WP1, WP2 (Log-Monitor) und WP3.1 erledigt** (2026-08-26). `Services/Observability/SelfMetrics/` mit `ISelfMetrics` + `SelfMetrics`: Loop-Gesundheit je (Loop, Server) — letzter Erfolg, letzter Versuch, Zyklusdauer, Zyklen, Fehlschläge, **Skips mit Grund** — plus benannte Zähler. Label-Satz technisch auf Loop/Server/Name begrenzt; ein Container-Label wäre bei 200 Containern der Weg in eine Monitoring-Störung durch Monitoring.
>
> **Die wichtigste Kennzahl ist `whiskers_self_loop_last_success_age_seconds`.** Fehlschläge werden nur gezählt, solange überhaupt etwas passiert; ein stehengebliebener Loop erzeugt gar nichts, und nur das *Alter* dieses Zeitstempels verrät ihn. Ebenso bewusst exportiert: `result="skipped"` — ein Server, den ein Loop überspringt, muss sichtbar bleiben, sonst liest sich „wird hier nicht überwacht" exakt wie „nichts zu berichten". Genau dieser Fall liegt heute bei vier Loops und Kubernetes-Servern vor.
>
> `/metrics` trägt die `whiskers_self_*`-Serien **vor** dem Inventar und außerhalb des try-Blocks: es sind prozesslokale Zähler, die nicht fehlschlagen können, und man braucht sie genau dann, wenn die Flotte nicht antwortet. Ein Test belegt das („The_self_series_survive_a_fleet_that_answers_nothing"), ein zweiter, dass der Endpunkt ohne Token weiterhin 401 liefert — die Nutzlast sagt jetzt zusätzlich, wie sich Whiskers verhält.
>
> **Gegenbeweis:** Timeout-Zähler abgeklemmt ⇒ `The_timeout_that_went_uncounted_for_six_days_is_counted` rot. Zurückgebaut.
>
> 🟢 **WP2 vollständig** (2026-08-26, zweiter Durchgang): Health, Metrics, CVE und ImageUpdate melden jetzt ebenfalls ihre Zyklen — **und die Kubernetes-Server, die sie überspringen**. Gemeinsamer Helfer `SelfMetricsFleetExtensions` mit festen Loop-Namen, damit die Labels nicht in `logmonitor` / `log-monitor` / `LogMonitor` auseinanderlaufen.
>
> Damit ist der konkrete blinde Fleck aus dem PRD geschlossen: Ein K8s-Host lieferte bisher gar keine Gesundheits-, Metrik-, CVE- oder Update-Daten, und das ist auf einem Dashboard nicht davon zu unterscheiden, dass dort nichts Auffälliges ist. Ein Test hält beide Richtungen fest — alle vier Loops markieren den K8s-Server, und der Docker-Host wird **nicht** fälschlich als übersprungen geführt, denn ein falscher Skip würde eine echte Lücke verdecken.
>
> **Offen aus SP-3:** WP3.2 (Persistenz in eigener Tabelle, additive Migration in beide Assemblies), WP4 (Ansicht), WP5 (Aktions-Zeitachse), WP6 (Leerlaufkosten belegen).
>
> ⚠️ **Bekannter Flake:** In etwa 2 von 11 vollen Läufen fällt genau **ein** Test aus, beim einzigen Mal, an dem der Name erfasst wurde, `BackupServiceTests.Validate_accepts_an_equal_or_older_schema`. In Isolation und in allen gezielten Wiederholungen grün, kein Bezug zu den Änderungen dieses Pakets erkennbar. Festgehalten, nicht behoben.

### WP2: Loop-Kennzahlen

**Zweck:** Die Kennzahl, die einen stehenden Loop verrät.
**Schätzung:** S (1 Tag).

1. **WP2.1:** Basisklasse oder Hilfsmittel für Hintergrund-Loops: misst Zyklusdauer, setzt `last_success_timestamp`, zählt übersprungene Objekte.
2. **WP2.2:** Alle vorhandenen Loops anschließen: LogMonitor, MetricsCollector, CveMonitor, ContainerHealthMonitor, ImageUpdate, Scheduler, Backup.
3. **WP2.3:** **Jeder Server erscheint in der Kennzahl, auch wenn der Loop ihn überspringt** — mit `result="skipped"` und Grund. Ohne das ist ein nicht laufender Loop von einem ruhigen nicht zu unterscheiden (siehe Kubernetes: dort überspringen vier Loops stillschweigend).

**Ergebnis:** Für jede Kombination aus Loop und Server existiert eine Aussage.

**Abnahme:** Ein Kubernetes-Server erscheint in allen Loop-Kennzahlen — teils mit `skipped`, nie gar nicht.

### WP3: Export und Speicherung

**Zweck:** Extern abgreifbar und ohne externe TSDB auswertbar.
**Schätzung:** S (1 Tag).

1. **WP3.1:** `/metrics` um `whiskers_self_*` erweitern, hinter dem bestehenden Scrape-Token.
2. **WP3.2:** Eigene Tabelle `SelfMetrics` (additive Migration, **beide** Migrations-Assemblies), Abtastung im Minutentakt, eigene Aufbewahrungsfrist.
3. **WP3.3:** Aufbewahrung in den vorhandenen Prune-Lauf einhängen.

**Ergebnis:** Die Kennzahlen überleben Neustarts und sind ohne Grafana lesbar.

**Abnahme:** `curl -H "Authorization: ..." :5100/metrics | grep -c whiskers_self_` liefert alle sieben Kernserien.

### WP4: Ansicht „Whiskers über sich selbst"

**Zweck:** Die Kennzahlen ohne externes Werkzeug nutzbar machen.
**Schätzung:** M (2 Tage).

1. **WP4.1:** Je Server: Aufrufrate, Latenz Median/p95, offene Aufrufe, Circuit-Zustand.
2. **WP4.2:** Je Loop: letzter Erfolg (als Alter, nicht als Zeitstempel — Alter liest sich richtig), Zyklusdauer, übersprungene Objekte.
3. **WP4.3:** Warnfarbe, wenn ein Alter drei Intervalle überschreitet.

**Ergebnis:** Ein Blick genügt für die Frage „läuft alles?".

### WP5: Aktions-Zeitachse

**Zweck:** Selbstverschuldete Last einer Aktion zuordnen.
**Schätzung:** M (2 Tage).

1. **WP5.1:** Audit-Log-Einträge des Servers (Deploy, Regeländerung, Neustart, Agentenaktion, Circuit-Öffnung) als Markierungen über die Metrikkurve legen.
2. **WP5.2:** **Alles in UTC rechnen**, Umrechnung nur bei der Darstellung. Ein Zeitversatz macht die Ansicht schlimmer als keine, weil sie falsche Zusammenhänge nahelegt.
3. **WP5.3:** Klick auf eine Markierung öffnet den Audit-Eintrag.

**Ergebnis:** „Was ist um 14:02 passiert?" ist in zehn Sekunden beantwortet.

**Abnahme:** Ein manuell ausgelöster Container-Neustart erscheint auf die Sekunde genau an der richtigen Stelle.

### WP6: Eigenkosten belegen

**Zweck:** Sicherstellen, dass die Selbstmessung nicht selbst zum Problem wird.
**Schätzung:** S (0,5 Tage).

1. **WP6.1:** Leerlaufmessung über 30 min mit und ohne Selbstmessung: CPU und Arbeitsspeicher.
2. **WP6.2:** Test, der beweist, dass ein Sammelzyklus **null** ausgehende Docker-Aufrufe erzeugt.
3. **WP6.3:** Zahl der Zeitreihen in `/metrics` als Kennzahl führen, für den Kardinalitätsvergleich zwischen Releases.

**Ergebnis:** Belegte Unbedenklichkeit statt Annahme.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_whiskers_self_status` — Loop-Gesundheit, Latenzen, offene Aufrufe, Circuit-Zustand. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Der Agent erkennt einen gestoppten Loop allein über dieses Werkzeug. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
Plan-0001 WP5 ──> WP1 ──> WP2 ──> WP3 ──> WP4
Plan-0002 WP2 ──┘                   └───> WP5
WP6 begleitend ab WP3
```

- **Extern blockiert von:** Plan-0001 (Zähler).
- **Blockiert:** SP-4 (Baseline braucht die Zeitreihen), GAP-4 (Instanzkennung).

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Vollständigkeit | `grep -c whiskers_self_ /metrics` | alle sieben Kernserien |
| Alter des letzten Erfolgs | Ansicht WP4.2 | < 3 × Intervall je Loop und Server |
| Offene Aufrufe | `whiskers_self_calls_in_flight` | ≤ Budget aus SP-1 |
| Zeitreihenzahl | WP6.3 | wächst nur mit Server- und Loop-Zahl |
| Eigenkosten | Leerlaufvergleich | < 1 Prozentpunkt CPU |
| Zeitachsen-Treue | bekanntes Ereignis | Versatz < 1 s |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Kennzahl bleibt konstant null | sieht gesund aus, ist unverdrahtet | Test, der einen künstlichen Timeout auslöst und beweist, dass der Zähler **um genau 1** steigt |
| Kardinalitätsexplosion | TSDB läuft voll, Scrape wird langsam | Label-Satz in WP1.2 technisch begrenzt; WP6.3 vergleicht je Release |
| Selbstmessung erzeugt Last | derselbe Fehler eine Ebene höher | WP6.2 als verpflichtender Test |
| Zeitversatz in der Zeitachse | falsche Ursachenzuordnung bei der nächsten Störung | WP5.2 + Abnahme gegen ein bekanntes Ereignis |
| Übersprungene Server fehlen ganz | ein nicht laufender Loop wirkt ruhig | WP2.3 — `skipped` mit Grund ist Pflicht |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 + WP2 | Kubernetes-Server erscheint in allen Loop-Kennzahlen |
| M2 | WP3 | sieben Kernserien auf `/metrics`, Tabelle wandert durch beide Migrations-Assemblies |
| M3 | WP4 | Ansicht zeigt einen künstlich gestoppten Loop als überaltert |
| M4 | WP5 | manueller Neustart liegt sekundengenau richtig |
| M5 | WP6 | Leerlaufvergleich protokolliert, Null-Aufruf-Test grün |

## Rückweg

Rein additiv: neue Tabelle, neue Serien, neue Ansicht. Bei Problemen kann der Export abgeschaltet werden, ohne die Erhebung zu stoppen. Die Erhebung selbst ist **nicht** abschaltbar zu bauen — ein Selbstmonitoring, das man einschalten muss, ist im Ernstfall aus.

## Definition of Done

- [ ] WP1–WP6 umgesetzt
- [ ] Sieben Kernserien auf `/metrics`, hinter Token
- [ ] Jede Loop-Server-Kombination liefert eine Aussage, auch `skipped`
- [ ] Zähler-Erhöhungstest je Kennzahl (nicht nur „Serie vorhanden")
- [ ] Null-Aufruf-Test für den Sammelzyklus grün
- [ ] Leerlaufkosten < 1 Prozentpunkt CPU, protokolliert
- [ ] Aktions-Zeitachse gegen ein bekanntes Ereignis verifiziert
- [ ] Migration in beiden Assemblies, gegen eine Kopie echter Daten geprüft
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
