# Plan-0005: Not-Aus (SP-5)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0005](../prd/0005-not-aus.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Als der Vorfall erkannt war, ging die Entschärfung über SSH auf dem Zielserver — an Whiskers vorbei, weil es dort keinen Weg gab, die eigenen Loops anzuhalten. Der Read-Only-Kill-Switch existiert nur für den Agenten.

Dieser Plan hat eine ungewöhnliche Reihenfolge: **Die Aufsichtsregel wird zuerst gebaut, der Schalter danach.** Ein Not-Aus ohne Aufsicht erzeugt genau den Zustand, den der Vorfall so teuer gemacht hat — alles ruhig, weil niemand hinsieht.

## Ziele

- Ein Server lässt sich in Sekunden aus der Beobachtung nehmen, ohne Neustart und ohne Zugriff auf den Server.
- Kein pausierter Server ist als gesund lesbar.
- Ein offener Circuit pausiert automatisch alle Loops für diesen Server.

## Arbeitspakete

### WP0: Aufsichtsregel

**Zweck:** Die Sicherung, bevor das Werkzeug scharf wird.
**Schätzung:** S (0,5 Tage). **Zuerst.**

1. **WP0.1:** Wiederkehrende Prüfung: alle Server, die länger als 24 h pausiert sind, werden gemeldet — täglich, solange der Zustand anhält.
2. **WP0.2:** Diese Regel ist vom Not-Aus **nicht** pausierbar. Technisch sicherstellen, nicht nur dokumentieren.

**Ergebnis:** Blindheit hat eine Verfallsdauer.

**Abnahme:** Server pausieren, Uhr vorstellen bzw. Schwelle testweise senken — Meldung erscheint, auch bei globalem Not-Aus.

### WP1: `ILoopSuspensionService`

**Zweck:** Ein Zustand, den alle Loops kennen.
**Schätzung:** S (1 Tag).

1. **WP1.1:** Zustand je Server und global, mit Ablaufzeitpunkt, Auslöser (Nutzer oder `automatisch`) und Grund.
2. **WP1.2:** Persistenz, damit die Pause einen Neustart überlebt.
3. **WP1.3:** **Fail-open:** Ein Fehler beim Abfragen des Zustands führt dazu, dass der Loop läuft — und wird gemeldet. Beobachtung ist der Normalzustand.
4. **WP1.4:** Abfrage in dieselbe Loop-Basis einhängen wie die Kennzahlen aus Plan-0003 WP2.1, damit kein Loop sie vergessen kann.

**Ergebnis:** Ein zentraler Schalter statt sieben Einzelabfragen.

**Abnahme:** Architekturtest schlägt fehl, sobald ein `BackgroundService` ohne Pausenabfrage existiert.

### WP2: Bedienung

**Zweck:** In Sekunden erreichbar.
**Schätzung:** S (1 Tag).

1. **WP2.1:** Schalter je Server in der Serveransicht, globaler Schalter in der Kopfzeile, jeweils mit Dauerauswahl (15 min / 1 h / 4 h / bis Widerruf).
2. **WP2.2:** „Bis Widerruf" erzeugt eine tägliche Erinnerung.
3. **WP2.3:** MCP-Werkzeug auf Level `write`, damit ein Mensch die Pause per Chat auslösen kann.
4. **WP2.4:** Globaler Not-Aus erfordert Admin-Recht.

**Ergebnis:** Der Weg von „Verdacht" zu „Whiskers ist raus" ist ein Klick.

### WP3: Automatische Pause bei offenem Circuit

**Zweck:** Die feine und die grobe Kelle verbinden.
**Schätzung:** S (0,5 Tage).

1. **WP3.1:** Öffnet der Circuit aus Plan-0001, pausieren alle Loops für diesen Server.
2. **WP3.2:** Schließt er, endet die Pause automatisch.
3. **WP3.3:** Auslöser wird als `automatisch` geführt und ist in Oberfläche und Audit vom Nutzerklick unterscheidbar.

**Abnahme:** Server abschalten → Circuit öffnet → alle Loops pausieren → Server anschalten → Betrieb kehrt zurück, alles ohne Eingriff.

### WP4: Rückkehr ohne Sturm

**Zweck:** Verhindern, dass die Pause die nächste Lastspitze erzeugt.
**Schätzung:** S (0,5 Tage).

1. **WP4.1:** Kein Nachholen versäumter Zyklen. Nach der Pause läuft der nächste reguläre Zyklus.
2. **WP4.2:** Wasserzeichen beim Pausenende auf `now - MaxLookback` setzen (Plan-0002 WP1), damit kein Riesenfenster entsteht.
3. **WP4.3:** Gestaffelter Wiederanlauf, wenn mehrere Server gleichzeitig zurückkehren.

**Abnahme:** Aufrufrate in den ersten fünf Minuten nach Pausenende überschreitet das Normalniveau nicht.

### WP5: Darstellung

**Zweck:** Drei unterscheidbare Zustände.
**Schätzung:** S (1 Tag).

1. **WP5.1:** `pausiert` ≠ `gesund` ≠ `ausgefallen` — eigene Darstellung mit Restzeit und Auslöser.
2. **WP5.2:** Auf dem Dashboard sichtbar, ohne in die Serveransicht wechseln zu müssen.
3. **WP5.3:** Gemeinsame Bildsprache mit `stopped-by-policy` aus attackResponse AR-3 — beides sind Zustände „bewusst nicht normal".

**Abnahme:** Ein Betreiber ohne Vorwissen erkennt auf dem Dashboard, welche Server nicht überwacht werden.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_suspension_status`, `suspend_server_loops`, `resume_server_loops` — Pausenzustand lesen und setzen, mit Dauer und Begründung. Stufe: read / write, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Ein per MCP gesetzter Not-Aus ist auf dem Zielserver messbar wirksam. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP0 ──> WP1 ──> WP2
          └───> WP4 ──> WP5
Plan-0001 WP4 (Circuit) ──> WP3
```

- **Extern blockiert von:** Plan-0001 WP4 für WP3; ohne Plan-0001 WP1/WP2 wirkt die Pause zudem verzögert (laufende Aufrufe enden nicht).
- **WP0 zwingend vor WP2.** Der Schalter darf nicht existieren, bevor die Aufsicht steht.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Wirksamkeit | Zugriffslog des Docker-Proxys **auf dem Zielserver** | 0 neue Anfragen nach 60 s |
| Vollständigkeit | `whiskers_self_calls_total` je Loop | alle Loops auf 0 |
| Rückkehr ohne Sturm | Aufrufrate nach Pausenende | ≤ Normalniveau |
| Vergessene Pausen | Zahl der Pausen „bis Widerruf" > 7 Tage | 0 |
| Sichtbarkeit | Dashboard | „pausiert" mit Restzeit |
| Neustartfestigkeit | Neustart im pausierten Zustand | Pause gilt weiter |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Pausierter Server wirkt gesund | der teuerste Fehler dieses Pakets | WP0 (nicht pausierbare Aufsicht) + WP5.1 |
| Ein Loop fragt nicht ab | Teilwirkung, wirkt wie Erfolg | WP1.4 + Architekturtest; Wirksamkeit auf dem **Zielserver** messen |
| Pausendienst fällt aus | gesamte Überwachung steht lautlos | WP1.3 fail-open plus Meldung |
| Nachhol-Sturm | die Pause erzeugt die nächste Last | WP4 |
| Automatisch und manuell verwechselt | Betreiber sucht die Ursache nicht | WP3.3 |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP0 | Aufsichtsregel meldet trotz globalem Not-Aus |
| M2 | WP1 + WP2 | Wirksamkeit im Proxy-Log des Zielservers belegt |
| M3 | WP3 | Circuit-Pause und automatische Rückkehr ohne Eingriff |
| M4 | WP4 + WP5 | keine Lastspitze nach Pausenende; Dashboard-Test mit unbeteiligter Person |

## Rückweg

Der Pausendienst ist additiv. Fällt er aus, laufen die Loops (fail-open). Eine dauerhafte Deaktivierung der Bedienung ist möglich, ohne den automatischen Circuit-Pfad zu verlieren.

## Definition of Done

- [ ] WP0–WP5 umgesetzt
- [ ] Wirksamkeit **auf dem Zielserver** gemessen: 0 neue Anfragen binnen 60 s
- [ ] Alle Loops nachweislich pausiert (Selbstmetriken je Loop auf 0)
- [ ] Aufsichtsregel greift auch bei globalem Not-Aus
- [ ] Keine Lastspitze nach Pausenende
- [ ] Pause überlebt Neustart
- [ ] Architekturtest verhindert Loops ohne Pausenabfrage
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
