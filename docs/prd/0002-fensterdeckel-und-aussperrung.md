# PRD-0002: Fensterdeckel & Aussperrung (SP-2)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md), Abschnitte „Die Wasserzeichen-Ratsche" und „Stufe 0"
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-2
- **Ersetzt:** —

## Problem / Motivation

Ein fehlgeschlagener Log-Abruf macht den nächsten Versuch **teurer**. Das Wasserzeichen `_lastLogCheck` wird nur nach erfolgreichem Abruf fortgeschrieben; `since` bleibt stehen, während `now` weiterläuft. Das angeforderte Fenster wächst mit jedem Fehlschlag um 60 Sekunden.

Damit ist der Zustand selbstverstärkend und ohne Eingriff endgültig: Fehlschlag → größeres Fenster → teurerer Abruf → sicherer Fehlschlag. Das erklärt, warum die Last am 20.08. innerhalb von zwei Minuten von 12 % auf 98 % sprang und in sechs Tagen kein einziges Mal zurückkam.

Verschärfend: `since` kostet dockerd die **ganze Datei**. Um das Fenster anzuwenden, liest und dekodiert der Daemon die JSON-Logdatei von vorn — bei 494 MB ein dreistelliger Megabyte-Betrag an Parsing, der keine einzige Zeile Ausgabe erzeugt, wenn das Fenster leer ist. Der `TailLines`-Deckel begrenzt die Übertragung, nicht die Arbeit.

Dritter Teil: ein Container, dessen Logs dauerhaft nicht lesbar sind, wird unbegrenzt weiter befragt. Es gibt keine Aussperrung — und keine Meldung. Der Vorfallsbericht nennt das Signal „eigene Log-Fetch-Timeouts, gezählt statt nur geloggt" als das **früheste und präziseste** von allen: es hätte nach drei Minuten angeschlagen statt nach sechs Tagen.

## Ziele

- Ein Fehlschlag macht den nächsten Versuch nicht teurer.
- Ein Container, dessen Logs nicht lesbar sind, wird nach wenigen Versuchen in Ruhe gelassen — und das wird gemeldet.
- Die Meldung kommt innerhalb von Minuten, nicht Tagen.

## Non-Goals

- **Keine** Garantie auf Lückenlosigkeit der Logzeilen. Im Ausfallfenster gehen Zeilen verloren; das ist die bewusst gewählte Seite des Kompromisses, weil sie aktuell ohnehin verloren gehen — nur dauerhaft.
- **Keine** Veränderung am Container (Log-Rotation, Truncate) — das ist SP-7 und bleibt Freigabe-pflichtig.
- **Keine** Änderung der Regel-Auswertung selbst (Muster, Schwellen) — das ist attackResponse AR-2.
- **Kein** Umbau auf Log-Streaming — ebenfalls AR-2.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Sieht nicht, dass ein Container aus der Überwachung gefallen ist. „Keine Alarme" ist heute nicht unterscheidbar von „wird nicht mehr geprüft".

### Whiskers selbst als Beobachtungsobjekt

- Pain Point: Die Warnung aus `FetchLogsAsync` versickert im eigenen Log. Sie ist das präziseste vorhandene Signal und wird nirgends gezählt.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Das `since`-Fenster ist hart gedeckelt: `since = max(letzterErfolg, now - MaxLookback)`, Default `MaxLookback` = 10 Minuten. | Must |
| FR-02 | Das Wasserzeichen wird auch im Fehlerfall fortgeschrieben, sodass ein Fehlschlag das nächste Fenster nicht vergrößert. | Must |
| FR-03 | Je (Server, Container) werden aufeinanderfolgende Fehlschläge gezählt; der Zähler wird bei Erfolg zurückgesetzt. | Must |
| FR-04 | Ab n Fehlschlägen in Folge (Default 3) wird der Container mit exponentiellem Backoff aus dem Scan genommen (5 min → 15 min → 60 min, Deckel 60 min). | Must |
| FR-05 | Die Aussperrung erzeugt **eine** Benachrichtigung mit Server, Container, Fehlschlagzahl und Dauer der Aussperrung. | Must |
| FR-06 | Die Rückkehr in den Scan nach erfolgreichem Abruf erzeugt ebenfalls eine Meldung (Entwarnung). | Must |
| FR-07 | Ausgesperrte Container sind in der Oberfläche als „nicht überwacht" erkennbar, nicht als „unauffällig". | Must |
| FR-08 | Verlorene Zeitfenster werden benannt: die Meldung nennt den Zeitraum, für den keine Zeilen ausgewertet wurden. | Should |
| FR-09 | Deckel, Schwelle und Backoff-Stufen sind konfigurierbar. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_log_scan_status` (read): Aussperrungen, Fehlschlagzähler und Alter des letzten erfolgreichen Scans je Container. `resume_log_scan` (write): eine Aussperrung vorzeitig aufheben. | Must |

## Nicht-Funktionale Anforderungen

- **Wirkung ohne SP-1 ist null:** solange abgelaufene Anfragen serverseitig weiterlaufen, entlastet keine Aussperrung. SP-1 ist harte Voraussetzung.
- **Keine Alarmflut:** Aussperrung und Entwarnung je Container höchstens einmal pro Zustandswechsel, nicht pro Zyklus.
- **Zustand überlebt keinen Neustart** (bewusst): nach einem Neustart wird jeder Container einmal neu probiert.

## User Stories

- **US-01:** Als Betreiber möchte ich innerhalb von Minuten erfahren, dass ein Container-Log nicht mehr lesbar ist, damit ich handeln kann, bevor der Server unter Last steht.
- **US-02:** Als Betreiber möchte ich in der Oberfläche unterscheiden können zwischen „keine Treffer" und „wird nicht geprüft".
- **US-03:** Als Betreiber möchte ich, dass ein wieder gesunder Container von selbst zurück in die Überwachung kommt.

### Flow für US-01

```
Given ein Container mit 500 MB Log, dessen Abruf > 15 s braucht
When der Log-Monitor drei Zyklen lang scheitert
Then wird der Container für 5 Minuten ausgesperrt,
     eine Meldung nennt Container, Server, "3 Fehlschläge", "5 min Pause",
     und das Fenster beim nächsten Versuch ist höchstens 10 Minuten breit
```

## Akzeptanzkriterien

- FR-01 bis FR-07 umgesetzt.
- Reproduktion: ein künstlich verlangsamter Container erzeugt innerhalb von **drei Zyklen** eine Meldung. Gemessen wird die Zeit von der ersten Verlangsamung bis zur Zustellung der Meldung — Zielwert < 4 Minuten.
- Über 20 Zyklen mit dauerhaft kaputtem Container: höchstens 1 Aussperrungsmeldung, keine Wiederholung je Zyklus.
- Die angeforderte Fensterbreite überschreitet in keinem Zyklus `MaxLookback` — nachweisbar im Zugriffslog des Docker-Proxys.
- MCP: Der Agent kann eine bestehende Aussperrung nennen und begründet aufheben; der Aufruf erscheint mit `CorrelationId` im Audit-Log.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Angefragte Fensterbreite | `since`-Parameter im Proxy-Zugriffslog (haproxy/socket-proxy) | ≤ `MaxLookback` | wächst über Zyklen ⇒ FR-01/FR-02 wirkungslos |
| Fehlschlagzähler je Container | `self:`-Zähler (SP-3) | 0 im Normalbetrieb | > 0 dauerhaft ohne Aussperrung ⇒ FR-04 greift nicht |
| Zahl ausgesperrter Container | `self:`-Zähler | 0 | > 0 **ohne** zugehörige Meldung ⇒ FR-05 kaputt (der gefährlichste Fall) |
| Zeit bis zur ersten Meldung | manuelle Messung im Reproduktionsfall | < 4 min | > 10 min ⇒ Schwelle oder Zustellweg falsch |
| Abrufdauer je Container über Zyklen | Proxy-Antwortzeit | konstant | monoton steigend ⇒ die Ratsche lebt noch |

## Woran ich sehe, dass es bricht

1. **Stille Aussperrung ist schlimmer als der Vorfall.** Wenn FR-05 nicht funktioniert, verschwindet ein Container geräuschlos aus der Überwachung — und alles sieht ruhig aus. **Gegenprobe im Betrieb:** eine wiederkehrende Prüfung „Zahl ausgesperrter Container > 0, aber keine offene Meldung dazu" muss selbst einen Alarm auslösen. Diese Prüfung ist wichtiger als die Aussperrung.
2. **Der Deckel verdeckt die Ursache.** Nach FR-01 ist ein Abruf immer billig — auch wenn der Container weiter jede Minute 200 MB schreibt. Das Symptom verschwindet, das Problem bleibt. **Messstelle:** Loggröße je Container (SP-7). Wächst sie weiter, während die Abrufe wieder schnell sind, ist der Deckel eine Betäubung, keine Heilung.
3. **Der „letzte erfolgreiche Scan" ist die entscheidende Zahl.** Nicht die Fehlschlagzahl. **Messstelle:** je Container das Alter des letzten erfolgreichen Scans; älter als 3 × Zyklusintervall ⇒ Meldung, unabhängig davon, welcher Mechanismus schuld ist.
4. **Backoff, der nie zurückkehrt.** Ein Fehler in der Rückkehr-Logik hält Container dauerhaft draußen. **Gegenprobe:** Test, der nach simulierter Erholung den Container im nächsten Zyklus wieder gescannt sieht — plus Entwarnungsmeldung.
5. **Der Zähler zählt das Falsche.** Wenn `LogFetchTimeout`-Fehler und normale Fehler (Container weg, Verbindung tot) in denselben Topf laufen, sperrt Whiskers gelöschte Container aus und meldet das als Problem. **Gegenprobe:** getrennte Zähler je Fehlerart; ein entfernter Container erzeugt keine Aussperrungsmeldung.

## Do's

- **SP-1 zuerst.** Ohne echten Abbruch ist dieses Paket Kosmetik.
- **Den Verlust benennen** (FR-08). Wer nicht sagt, welche Minuten nicht ausgewertet wurden, verkauft eine Lücke als Ruhe.
- **Getrennte Fehlerarten zählen** — Timeout, Verbindungsfehler, Container weg.
- **Zustandswechsel melden, nicht Zustände** — sonst Alarmflut.

## Don'ts

- **Nicht** das Wasserzeichen einfach auf `now` setzen und schweigen. Der Sprung im Fenster ist ein Datenverlust und gehört gemeldet.
- **Nicht** `MaxLookback` groß wählen, „damit nichts verloren geht". 10 Minuten × 200 Zeilen ist die Obergrenze dessen, was ein Zyklus ohnehin auswerten kann.
- **Nicht** ausgesperrte Container in der Oberfläche wie gesunde darstellen.
- **Keine** unbegrenzte Backoff-Steigerung. Ein Deckel von 60 Minuten stellt sicher, dass ein reparierter Container zeitnah zurückkommt.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 (hart).
- **Blockiert:** nichts direkt; liefert Signale an SP-3/SP-4 und meldet in das Incident-Objekt aus attackResponse AR-1, sobald vorhanden.

## Offene Fragen

- **F-01:** Soll eine Aussperrung nach Ablauf des maximalen Backoffs dauerhaft werden (mit deutlicher Meldung) oder ewig weiter probieren? Vorschlag: weiter probieren, aber die Meldung nach 24 h eskalieren.
