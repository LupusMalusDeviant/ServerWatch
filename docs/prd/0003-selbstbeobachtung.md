# PRD-0003: Selbstbeobachtung (SP-3)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte, Whiskers-Entwickler
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md), Abschnitt „Die blinde Stelle"
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-3
- **Ersetzt:** —

## Problem / Motivation

Whiskers exportiert einen Prometheus-Endpunkt, der die Container-Inventur der gesamten Flotte enthält — und kein einziges Datum über Whiskers selbst. Es gibt keine Zahl für: wie viele Docker-Aufrufe pro Minute erzeugt Whiskers, wie lange dauern sie, wie viele laufen gleichzeitig, wie lange braucht ein Scan-Zyklus, wie oft läuft ein Abruf in ein Timeout.

Der Vorfall vom 26.08. bestand sechs Tage, obwohl die Warnung „timed out after 15s" die ganze Zeit geschrieben wurde. Sie versickerte, weil niemand sie zählt. Der Vorfallsbericht formuliert es als Signal 3 und nennt es das früheste und präziseste — drei Minuten statt sechs Tage.

Der zweite Teil des Problems ist die Ursachensuche. Whiskers kennt seine eigene Aktionshistorie (Audit-Log) und die Serverzustände (`ServerMetrics`) — beide in derselben Datenbank, nie zusammen dargestellt. Am 20.08. sprang die Last innerhalb von zwei Minuten. Eine Ansicht, die eigene Aktionen über die Metrikkurve legt, hätte den Verursacher sofort gezeigt.

## Ziele

- Whiskers beobachtet sich selbst mit derselben Ernsthaftigkeit wie die überwachten Server.
- Die eigenen Kennzahlen sind extern abgreifbar (Prometheus) und intern bewertbar (dieselbe Schwellwert-Engine wie SP-4).
- Selbstverschuldete Lasten sind in der Oberfläche einer eigenen Aktion zuordenbar.

## Non-Goals

- **Keine** Bewertung/Alarmierung in diesem Paket — dieses Paket liefert die Zahlen, SP-4 bewertet sie.
- **Kein** Tracing-Framework (OpenTelemetry, Jaeger). Zähler und Histogramme reichen.
- **Keine** Langzeitarchivierung der eigenen Metriken über die bestehende Retention hinaus.
- **Keine** Änderung am bestehenden Inventar-Teil von `/metrics`.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Kann nicht beantworten, ob eine Serverlast von Whiskers stammt oder von der Nutzlast.

### Whiskers-Entwickler

- Pain Point: Regressionen im Lastverhalten sind unsichtbar, bis ein Server umfällt. Es gibt keine Zahl, gegen die ein Release verglichen werden könnte.

### Betreiber mit vorhandenem Grafana/VictoriaMetrics

- Kontext: hat bereits eine TSDB, in die Whiskers ohnehin schreibt.
- Pain Point: Muss Whiskers als blinden Fleck in seinen Dashboards führen.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Ein `ISelfMetrics`-Dienst sammelt in-Prozess: Docker-/API-Aufrufe je Server und Operationsart (Zähler), Antwortzeiten (Histogramm), gleichzeitig laufende Aufrufe (Gauge), Timeouts, verworfene Doppelanfragen, Circuit-Zustand. | Must |
| FR-02 | Je Hintergrund-Loop und Server: Zyklusdauer, Zeitpunkt des letzten erfolgreichen Durchlaufs, Zahl der übersprungenen Objekte. | Must |
| FR-03 | Alle Werte aus FR-01/FR-02 erscheinen auf `/metrics` mit Präfix `whiskers_self_`. | Must |
| FR-04 | Die Werte werden zusätzlich als Zeitreihe in `ServerMetrics` bzw. einer eigenen Tabelle persistiert, damit sie ohne externe TSDB auswertbar sind. | Must |
| FR-05 | Eine Ansicht „Whiskers über sich selbst" zeigt je Server: Aufrufrate, Latenz-Median/p95, offene Aufrufe, Circuit-Zustand, letzter erfolgreicher Scan je Loop. | Must |
| FR-06 | Die Aktions-Zeitachse überlagert Audit-Log-Einträge (Deploy, Regeländerung, Neustart, Agentenaktion) mit den Metrikkurven desselben Servers. | Must |
| FR-07 | Der Endpunkt bleibt hinter dem bestehenden Scrape-Token; `whiskers_self_`-Werte enthalten keine Container- oder Kundennamen. | Must |
| FR-08 | Ein „Was hat Whiskers heute getan"-Tagesbericht fasst eigene Aktionen und Selbstdrosselungen zusammen. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_whiskers_self_status` (read): je Loop und Server Alter des letzten Erfolgs, Zyklusdauer, Latenz-Median/p95, offene Aufrufe, Circuit-Zustand. Das ist das wichtigste Diagnosewerkzeug des gesamten Katalogs. | Must |

## Nicht-Funktionale Anforderungen

- **Vernachlässigbare Kosten:** Die Selbstmessung darf < 1 % CPU und < 20 MB RAM zusätzlich kosten; nachzuweisen durch Vorher-/Nachher-Messung im Leerlauf.
- **Kein Rückkopplungsrisiko:** Die Selbstmessung darf keine Docker-Aufrufe erzeugen. Sie liest ausschließlich prozessinterne Zähler.
- **Kardinalität begrenzt:** Labels nur `server`, `loop`, `operation`, `result` — niemals Container-ID oder -Name.

## User Stories

- **US-01:** Als Betreiber möchte ich sehen, wie viel Last Whiskers auf einem Server erzeugt, um sie von der Nutzlast zu unterscheiden.
- **US-02:** Als Betreiber möchte ich beim Blick auf eine Lastspitze sofort sehen, welche eigene Aktion zeitlich davor lag.
- **US-03:** Als Entwickler möchte ich zwei Releases anhand der Aufrufrate je Server vergleichen können.

### Flow für US-02

```
Given eine Lastspitze auf einem Server um 14:02
When der Betreiber die Serveransicht öffnet
Then liegt auf der CPU-Kurve eine Markierung "14:00 Log-Regel geändert (Nutzer X)"
     und der Zeitversatz ist unmittelbar ablesbar
```

## Akzeptanzkriterien

- FR-01 bis FR-07 umgesetzt.
- `curl -H "Authorization: ..." :5100/metrics | grep whiskers_self_` liefert mindestens: `calls_total`, `call_duration_seconds`, `calls_in_flight`, `timeouts_total`, `cycle_duration_seconds`, `last_success_timestamp`, `circuit_open`.
- Der Vorfall vom 20.08. wäre erkennbar gewesen: im Reproduktionsfall steigen `timeouts_total` und `calls_in_flight` innerhalb von drei Zyklen sichtbar an.
- Leerlaufmessung: CPU-Differenz mit/ohne Selbstmessung < 1 Prozentpunkt über 30 Minuten.
- MCP: Ein Agentenlauf erkennt allein über `get_whiskers_self_status` einen künstlich gestoppten Loop und benennt ihn.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Vollständigkeit der Serie | `grep -c whiskers_self_ /metrics` | alle 7 Kernserien vorhanden | eine fehlt ⇒ ein Loop meldet nicht |
| `last_success_timestamp` je Loop/Server | `/metrics` bzw. Ansicht FR-05 | Alter < 3 × Intervall | älter ⇒ Loop steht, unabhängig von Fehlermeldungen |
| `calls_in_flight` | `/metrics` | ≤ Budget aus SP-1 | > Budget ⇒ das Budget wird umgangen |
| Eigenkosten der Messung | Leerlauf-CPU vorher/nachher | < 1 Prozentpunkt | > 3 ⇒ Kardinalität oder Sammelfrequenz falsch |
| Kardinalität | Zahl der Zeitreihen in `/metrics` | wächst nur mit Server-/Loop-Zahl | wächst mit Containern ⇒ FR-07 verletzt, TSDB läuft voll |
| Zeitachsen-Zuordnung | Ansicht FR-06 gegen bekanntes Ereignis | Aktion liegt zeitlich korrekt | Versatz > 1 min ⇒ Zeitzonen-/UTC-Fehler |

## Woran ich sehe, dass es bricht

1. **Metriken, die immer gleich aussehen, sind kaputt.** Ein Zähler, der nie steigt, und ein Gauge, der konstant 0 zeigt, sind der Normalfall bei falscher Verdrahtung — und sie sehen aus wie ein gesundes System. **Gegenprobe:** ein Test, der einen künstlichen Timeout auslöst und beweist, dass `timeouts_total` **um genau 1 steigt**. Ohne diesen Test ist keiner der Werte belastbar.
2. **Die gefährlichste Zahl ist die, die fehlt.** `last_success_timestamp` ist wichtiger als jeder Fehlerzähler: Fehler werden gezählt, wenn etwas passiert — ein stehender Loop erzeugt gar nichts. **Betriebsprüfung:** eine Regel, die auf das *Alter* dieses Wertes schaut, nicht auf seinen Inhalt.
3. **Kardinalitäts-Explosion killt die TSDB.** Ein versehentliches Container-Label bei 200 Containern × 5 Operationen × 4 Ergebnissen sind 4.000 Serien pro Server. **Messstelle:** Zahl der Zeitreihen nach jedem Release vergleichen; Sprung > 20 % ohne neue Server ⇒ Label-Fehler.
4. **Selbstmessung, die selbst Last erzeugt.** Wenn die Sammlung Docker-Aufrufe macht (etwa um Containernamen aufzulösen), baut dieses Paket denselben Fehler ein, den es sichtbar machen soll. **Gegenprobe:** Test, der beweist, dass ein Sammelzyklus **null** ausgehende Aufrufe erzeugt.
5. **Zeitachse ohne Wahrheit.** Wenn Audit-Zeitstempel lokal und Metriken in UTC liegen, zeigt FR-06 systematisch falsche Zusammenhänge — und führt bei der nächsten Ursachensuche in die Irre. Das ist schlimmer als keine Ansicht. **Gegenprobe:** ein bekanntes Ereignis (manueller Neustart) muss in der Ansicht auf die Sekunde am richtigen Punkt liegen.

## Do's

- **Erst die Zahlen, dann die Ansicht.** FR-01–FR-04 sind ohne UI nützlich; die Ansicht ohne belastbare Zahlen ist Dekoration.
- **`last_success_timestamp` für jeden Loop von Anfang an**, auch für die, die heute problemlos laufen.
- **Alles in UTC**, Umrechnung nur bei der Darstellung.
- **Die Zähler aus SP-1 wiederverwenden** statt zweimal zu messen.

## Don'ts

- **Keine** Container-Namen oder -IDs als Metrik-Label.
- **Nicht** die Selbstmetriken hinter einem Feature-Flag verstecken, das im Betrieb aus ist. Ein Selbstmonitoring, das man einschalten muss, ist im Ernstfall aus.
- **Nicht** OpenTelemetry einführen, um „es richtig zu machen". Sieben Zeitreihen lösen dieses Problem.
- **Nicht** auf `/metrics` ohne Token exponieren — die Serien verraten die Flottengröße.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 (liefert die Zähler aus FR-01).
- **Blockiert:** SP-4 (Baseline braucht Zeitreihen), GAP-4 (Multi-Replica ohne Selbstbeobachtung ist nicht diagnostizierbar).

## Offene Fragen

- **F-01:** Eigene Tabelle oder `ServerMetrics` mitbenutzen? Vorschlag: eigene Tabelle, weil Retention und Kardinalität anders sind.
- **F-02:** Soll der Tagesbericht (FR-08) über die bestehenden Kanäle gehen oder nur in der App liegen? Vorschlag: in der App, opt-in für Kanäle.
