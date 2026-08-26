# Plan-0004: Host- & Baseline-Alarme (SP-4)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0004](../prd/0004-host-und-baseline-alarme.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

`EvaluateAlertsAsync(ContainerInfo, ContainerStats, ...)` bewertet ausschließlich je Container. Der Vorfall wurde 8.900-mal gemessen und nie bewertet, weil `dockerd` in keinem Container läuft.

Dieser Plan hat eine Besonderheit: **Der Prüfstand existiert bereits.** Die Metriken vom 20.–26.08. liegen in der Datenbank (Aufbewahrung 90 Tage). Sie sind die Messlatte — eine Regel, die diesen Vorfall an den echten Daten nicht findet, ist unbrauchbar, egal wie plausibel sie aussieht.

## Ziele

- Der bekannte Vorfall wird an den aufgezeichneten Daten innerhalb von 20 Minuten gefunden.
- Abweichungen vom Normalverhalten werden erkannt, nicht nur Grenzwerte.
- Die Fehlalarmquote bleibt so niedrig, dass die Meldungen gelesen werden.

## Arbeitspakete

### WP0: Prüfstand sichern

**Zweck:** Ohne die Daten vom Vorfall gibt es keine belastbare Abnahme.
**Schätzung:** S (0,5 Tage). **Zuerst, vor jeder Zeile Regel-Code.**

1. **WP0.1:** `ServerMetrics` für BurgCloud, 19.–27.08., exportieren und als Testdatensatz ins Repo legen (anonymisiert, falls nötig).
2. **WP0.2:** Wiedergabe-Vorrichtung: Zeitreihe zeitgerafft durch die Regel-Engine schicken, Ausgabe = Liste erzeugter Meldungen mit Zeitstempeln.

**Ergebnis:** Ein reproduzierbarer Prüfstand mit einem bekannten richtigen Ergebnis.

**Abnahme:** Der Datensatz enthält den Lastsprung am 20.08. um 14:02 und die Entlastung am 26.08. um 15:07.

### WP1: Host-Schwellen

**Zweck:** Die Lücke schließen, durch die der Vorfall gefallen ist.
**Schätzung:** S (1 Tag).

1. **WP1.1:** Bewertung für Host-CPU, -RAM und Load analog zum vorhandenen `disk:{server}`-Schlüssel in `MetricsCollectorService`.
2. **WP1.2:** Auslösung erst nach anhaltender Überschreitung (Default 10 min), nicht bei Einzelmessung.
3. **WP1.3:** Schwellen je Server konfigurierbar, Defaults für 2-Kern-Maschinen.

**Abnahme:** Wiedergabe des Prüfstands erzeugt eine Meldung ≤ 20 min nach 14:02.

### WP2: Unerklärte Host-Last

**Zweck:** Das spezifischste Signal des Vorfalls — es nennt die Ursachenklasse mit.
**Schätzung:** M (1,5 Tage).

1. **WP2.1:** Host-CPU und Summe der Container-CPU **auf dieselbe Normierung bringen** (Prozent eines Kerns vs. aller Kerne). Das ist die eigentliche Arbeit und die Hauptfehlerquelle.
2. **WP2.2:** Differenz über Schwelle über Dauer ⇒ Meldung mit dem Text, dass ein Host-Prozess der Verursacher ist.
3. **WP2.3:** Formulierung als Hinweis, nicht als Beweis — kurzlebige Container fehlen in der Summe.

**Abnahme:** Auf einem Leerlaufserver liegt die Differenz nahe null; `stress-ng` auf dem Host erzeugt eine Meldung mit korrekter Ursachenklasse.

### WP3: Rollende Baseline

**Zweck:** Abweichung statt Grenzwert.
**Schätzung:** M (2 Tage).

1. **WP3.1:** EWMA plus Standardabweichung je Server und Metrik, laufend fortgeschrieben, ein Wert je Metrik — keine Fensterhaltung im Speicher.
2. **WP3.2:** z-Score-Schwelle, Default kalibriert an echten Flottendaten (nicht 3σ aus dem Lehrbuch übernehmen).
3. **WP3.3:** Anlernphase 48 h mit sichtbarem Hinweis „lernt noch" statt Fehlalarmen.
4. **WP3.4:** **Schutz gegen das Mitlernen eines Fehlers:** Steigt der EWMA-Mittelwert über die absolute Schwelle aus WP1, wird das selbst gemeldet. Sonst schweigt die Abweichungserkennung nach sechs Tagen Dauerlast genau dann, wenn sie gebraucht wird.

**Abnahme:** Wiedergabe des Prüfstands: die Baseline meldet den Sprung am 20.08. **und** meldet am 23.08., dass ihr Mittelwert über der absoluten Schwelle liegt.

### WP4: API-Antwortzeit als Signal

**Zweck:** Der Fingerabdruck eines überlasteten Daemons, unabhängig vom Verursacher.
**Schätzung:** S (0,5 Tage).

1. **WP4.1:** Rollenden Median der Docker-API-Antwortzeit je Server aus den Selbstmetriken (SP-3) auswerten.
2. **WP4.2:** Sprung um Faktor n über Dauer ⇒ Meldung.

**Abnahme:** Ein künstlich verlangsamter Docker-Proxy erzeugt binnen zwei Auswertungen eine Meldung.

### WP5: Meldungsqualität und Entwarnung

**Zweck:** Verhindern, dass die neuen Signale abtrainiert werden.
**Schätzung:** S (1 Tag).

1. **WP5.1:** Je Server und Metrik höchstens eine offene Meldung; Verschärfung eskaliert, wiederholt nicht.
2. **WP5.2:** Entwarnung beim Unterschreiten, Meldung wird geschlossen.
3. **WP5.3:** Jede Meldung trägt Ist-Wert, Schwelle/Baseline, Dauer und — wo bekannt — Ursachenklasse.
4. **WP5.4:** Kennzahl „offene Meldungen älter als 7 Tage" — steigt sie monoton, ist der Schließpfad kaputt.

**Abnahme:** Ein 5-Minuten-Build-Peak erzeugt keine Meldung; ein 30-Minuten-Dauerzustand erzeugt genau eine, die beim Ende geschlossen wird.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `list_active_alerts`, `get_alert_rules` — offene Meldungen und Regeln lesen. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Der Agent gibt Ist-Wert, Schwelle und Dauer korrekt wieder. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP0 ──> WP1 ──> WP2
   └──> WP3 (braucht Zeitreihen aus SP-3)
SP-3 ──> WP3, WP4
WP1..WP4 ──> WP5
```

- **Extern blockiert von:** SP-3 (Zeitreihen und Selbstmetriken).
- **Blockiert:** SP-6 (misst gegen diese Signale).
- **Teilen statt doppeln:** Die Fenster-/Schwellenmechanik wird gemeinsam mit attackResponse AR-2 gebaut, nicht zweimal.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Nachstellung des Vorfalls | Prüfstand WP0 | Meldung ≤ 20 min nach Beginn |
| Meldungen je Server und Woche | Alarm-Historie | ≤ 2 |
| Trefferquote | Stichprobe: war die Meldung berechtigt? | ≥ 80 % |
| Offene Meldungen > 7 Tage | Kennzahl WP5.4 | nicht monoton steigend |
| Differenz auf Leerlaufserver | WP2 | nahe null |
| Baseline-Mittelwert vs. absolute Schwelle | WP3.4 | Mittelwert unter der Schwelle |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Fehlalarme | Signale werden ignoriert — der eigentliche Ausfallmodus | Dauer statt Momentaufnahme; wöchentliche Trefferquoten-Stichprobe als feste Routine |
| Baseline lernt den Fehler | schweigt bei Dauerlast | WP3.4 |
| Normierungsfehler in WP2 | Dauerlärm oder Dauerschweigen | Leerlauf-Gegenprobe + `stress-ng`-Gegenprobe, beide verpflichtend |
| Prüfstand geht verloren | keine belastbare Abnahme mehr möglich | WP0 zuerst, Daten ins Repo |
| Regel im Test grün, im Feld blind | falsche Sicherheit | ausschließlich die Wiedergabe echter Daten zählt als Abnahme |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP0 | Prüfstand läuft, Datensatz enthält den Vorfall |
| M2 | WP1 + WP2 | Wiedergabe meldet ≤ 20 min nach 14:02 mit Ursachenklasse |
| M3 | WP3 | Baseline meldet Sprung **und** eigenen Drift über die Schwelle |
| M4 | WP4 + WP5 | Build-Peak schweigt, Dauerzustand meldet einmal und wird geschlossen |
| M5 | Feldlauf | 14 Tage auf der echten Flotte, Trefferquote dokumentiert |

## Rückweg

Alle Regeln sind einzeln abschaltbar und je Server konfigurierbar. Erweist sich die Baseline im Feld als zu laut, wird sie deaktiviert — die absolute Schwelle aus WP1 bleibt in jedem Fall aktiv, sie ist die eigentliche Lückenschließung.

## Definition of Done

- [ ] WP0–WP5 umgesetzt
- [ ] **Wiedergabe der echten Daten vom 20.–26.08. erzeugt die Meldung innerhalb von 20 Minuten** — ohne diesen Nachweis gilt der Plan nicht als erfüllt
- [ ] `stress-ng`-Gegenprobe erzeugt „Host-Prozess"-Meldung
- [ ] Leerlaufserver zeigt Differenz nahe null
- [ ] 5-Minuten-Peak erzeugt keine Meldung
- [ ] Entwarnung schließt Meldungen; Kennzahl „offen > 7 Tage" stabil
- [ ] Trefferquote über 14 Tage Feldlauf ≥ 80 %, dokumentiert
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
