# PRD-0004: Host- & Baseline-Alarme (SP-4)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md), Abschnitte „Die blinde Stelle" und „Fünf Signale"
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-4
- **Ersetzt:** —

## Problem / Motivation

Whiskers bewertet Schwellwerte ausschließlich **je Container**. Die Signatur sagt alles: `EvaluateAlertsAsync(ContainerInfo c, ContainerStats stats, ...)`. Für Platten existiert ein Host-Schlüssel (`disk:{server}`), für CPU und RAM des Hosts nichts.

Verbrannt hat die CPU aber `dockerd` — ein Host-Prozess, der in keinem Container läuft. Ergebnis: sechs Tage, rund 8.900 Messpunkte für BurgCloud, praktisch jeder über 98 %, kein einziger bewertet. Aufgefallen ist es, weil ein Mensch auf die Übersichtsseite geschaut hat.

Eine reine Schwelle greift aber zu kurz. Ein Server, der normal bei 70 % läuft, hat bei 85 % ein Problem; einer, der bei 5 % läuft, schon bei 30 %. Und das spezifischste Signal des ganzen Vorfalls ist ein Vergleich, kein Grenzwert: **Host-Last, die kein Container erklärt** — beide Zahlen liegen bereits vor, Host-CPU und die Summe der Container-Stats. Klaffen sie auseinander, ist der Verursacher ein Host-Prozess. Dieses Signal nennt die Ursachenklasse gleich mit.

## Ziele

- Ein Server, der dauerhaft über der Schwelle steht, wird gemeldet — ohne dass ein Mensch hinschaut.
- Abweichungen vom normalen Verhalten eines Servers werden erkannt, nicht nur Überschreitungen absoluter Grenzen.
- Host-Last ohne Container-Erklärung wird als eigene, benannte Ursachenklasse gemeldet.

## Non-Goals

- **Keine** ML-basierte Anomalieerkennung. EWMA und z-Score genügen; Netdata-Parität ist nicht das Ziel.
- **Keine** automatische Gegenmaßnahme auf dem Server — Meldung und Vorschlag, mehr nicht (SP-6 regelt die Wirkungskontrolle für die Fälle, in denen Whiskers *sich selbst* drosselt).
- **Keine** Änderung der Metrik-Erhebung selbst (Intervalle, Quellen).
- **Keine** Kapazitätsplanung oder Trendprognose.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Verlässt sich darauf, dass ein Monitoring meldet. Aktuell misst es nur.

### Betreiber kleiner Server

- Kontext: 1–4 Kerne, Nutzlast und Verwaltung teilen sich die Maschine.
- Pain Point: Feste Schwellen sind entweder zu laut (kurze Build-Spitzen) oder zu spät.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Host-Schwellwerte für CPU, RAM und Load analog zum vorhandenen `disk:{server}`-Schlüssel. | Must |
| FR-02 | Eine Schwelle löst erst nach anhaltender Überschreitung aus (Default: 10 Minuten über Schwelle), nicht bei einer Einzelmessung. | Must |
| FR-03 | Signal „unerklärte Host-Last": Host-CPU minus Summe der Container-CPU über Schwelle X über Dauer Y ⇒ Meldung mit der Formulierung, dass ein Host-Prozess der Verursacher ist. | Must |
| FR-04 | Rollende Baseline je Server und Metrik (EWMA über 7 Tage + Standardabweichung); Abweichung über z-Score-Schwelle erzeugt eine eigene, von FR-01 unabhängige Meldung. | Must |
| FR-05 | Die Baseline hat eine Anlernphase (Default 48 h), in der sie meldet, dass sie noch lernt, statt Fehlalarme zu erzeugen. | Must |
| FR-06 | Signal „Docker-API-Antwortzeit": rollender Median je Server; Sprung um Faktor n ⇒ Meldung. | Must |
| FR-07 | Alle Meldungen tragen Server, Metrik, Ist-Wert, Schwelle/Baseline, Dauer und — wo bekannt — die Ursachenklasse. | Must |
| FR-08 | Schwellen und z-Score-Grenzen sind je Server konfigurierbar; Defaults sind für 2-Kern-Maschinen gewählt. | Should |
| FR-09 | Meldungen laufen in das Incident-Objekt (attackResponse AR-1), sobald es existiert; bis dahin in die vorhandene Alarm-Historie. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `list_active_alerts` und `get_alert_rules` (read). Ein schreibendes `set_host_threshold` bleibt vorerst **ausgeschlossen** — eine Regeländerung durch den Agenten ist ein Eingriff in die eigene Beobachtung (siehe PRD-0013 F-02). | Must |

## Nicht-Funktionale Anforderungen

- **Keine Alarmflut:** Je Server und Metrik höchstens eine offene Meldung; Eskalation statt Wiederholung.
- **Ehrliche Entwarnung:** Fällt der Wert zurück, wird die Meldung geschlossen und das sichtbar gemacht.
- **Rechenaufwand vernachlässigbar:** EWMA ist ein Wert pro Metrik und Server, keine Fensterhaltung im Speicher.

## User Stories

- **US-01:** Als Betreiber möchte ich gemeldet bekommen, wenn ein Server dauerhaft am Anschlag läuft — auch wenn kein Container dafür verantwortlich ist.
- **US-02:** Als Betreiber möchte ich, dass mir eine ungewöhnliche Abweichung auffällt, bevor sie eine absolute Schwelle reißt.
- **US-03:** Als Betreiber möchte ich in der Meldung lesen, in welche Richtung ich suchen muss.

### Flow für US-01

```
Given Host-CPU 98 %, Summe aller Container-CPU 12 %
When der Zustand 10 Minuten anhält
Then erscheint eine Meldung: "burgcloud: Host-CPU 98 % seit 10 min,
     Container erklären nur 12 % — Verursacher ist ein Host-Prozess"
```

## Akzeptanzkriterien

- FR-01 bis FR-07 umgesetzt.
- Nachstellung des Vorfalls gegen die aufgezeichneten Daten vom 20.–26.08.: die Regeln erzeugen eine Meldung mit Zeitstempel **innerhalb von 20 Minuten** nach 14:02 UTC. Ohne diese Nachstellung gilt das Paket nicht als fertig.
- Ein 5-Minuten-Build-Peak auf einem Entwicklungsserver erzeugt **keine** Meldung.
- Baseline-Anlernphase: ein frisch aufgenommener Server erzeugt in den ersten 48 h keine Baseline-Alarme, sondern einen Hinweis „lernt noch".
- MCP: Der Agent kann offene Meldungen samt Schwelle, Ist-Wert und Dauer wiedergeben, ohne Werte zu erfinden.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Nachstellung des bekannten Vorfalls | Wiedergabe der Zeitreihe 20.–26.08. gegen die Regel-Engine | Meldung ≤ 20 min nach Beginn | keine Meldung ⇒ die Regel taugt nicht, egal wie sie aussieht |
| Fehlalarmrate | Zahl der Meldungen je Server und Woche | ≤ 2 | > 10 ⇒ Schwellen/z-Score zu eng, wird ignoriert werden |
| Verhältnis Meldung ↔ Realität | Stichprobe: jede Meldung einer Woche prüfen | ≥ 80 % begründet | < 50 % ⇒ die Meldungen werden abtrainiert, das Signal ist tot |
| Baseline-Stabilität | EWMA-Verlauf je Server | glatt, folgt dem Tagesgang | springt bei jedem Peak ⇒ Glättungsfaktor falsch |
| Unerklärte Last, Gegenprobe | absichtlich `stress-ng` auf dem Host starten | Meldung mit Ursachenklasse „Host-Prozess" | keine Meldung ⇒ FR-03 rechnet falsch |
| Entwarnung | nach Ende der Last | Meldung wird geschlossen | bleibt offen ⇒ die Historie füllt sich mit Geistern |

## Woran ich sehe, dass es bricht

1. **Die Nachstellung ist das einzige harte Kriterium.** Jede Regel lässt sich so bauen, dass sie im Test grün ist. Der Beweis ist: dieselbe Regel gegen die **echten aufgezeichneten Daten** des bekannten Vorfalls, mit einer Meldung an der richtigen Stelle der Zeitachse. Fällt dieser Test weg, ist das ganze Paket unbelegt.
2. **Fehlalarme sind der eigentliche Ausfallmodus.** Ein Alarmsystem stirbt nicht daran, dass es schweigt, sondern daran, dass es zu oft ruft und dann ignoriert wird. **Messstelle:** Meldungen je Server und Woche, plus eine ehrliche Stichprobe „war das berechtigt?". Unter 50 % Trefferquote ist das Signal praktisch tot, auch wenn technisch alles funktioniert.
3. **Die Baseline lernt den Fehler mit.** Läuft ein Server sechs Tage bei 98 %, wird 98 % zur Normalität — und die Abweichungserkennung schweigt genau dann, wenn sie gebraucht wird. **Gegenmaßnahme und Messstelle:** die absolute Schwelle (FR-01) bleibt unabhängig von der Baseline bestehen; eine Baseline, deren Mittelwert über die absolute Schwelle steigt, muss selbst eine Meldung erzeugen.
4. **Entwarnungen, die nie kommen.** Offene Meldungen ohne Schließung machen die Historie wertlos und verdecken neue Fälle. **Messstelle:** Zahl offener Meldungen älter als 7 Tage; steigt sie monoton, ist der Schließpfad kaputt.
5. **Die Differenz aus FR-03 wird negativ oder unsinnig.** Container-Stats und Host-CPU stammen aus verschiedenen Quellen mit verschiedenen Intervallen und Normierungen (Prozent von einem Kern vs. von allen). Ein Vorzeichenfehler erzeugt entweder Dauerlärm oder Dauerschweigen. **Gegenprobe:** auf einem Leerlaufserver muss die Differenz nahe 0 liegen; ist sie konstant hoch oder negativ, stimmt die Normierung nicht.

## Do's

- **Mit der Nachstellung anfangen**, nicht mit der Regel. Die aufgezeichneten Daten sind vorhanden — sie sind der Prüfstand.
- **Absolute Schwelle und Baseline getrennt halten.** Sie fangen unterschiedliche Fehler; eine ersetzt die andere nicht.
- **Dauer statt Momentaufnahme** (FR-02). Ein Peak ist kein Vorfall.
- **Die Ursachenklasse in die Meldung schreiben.** „Host-CPU hoch" hilft wenig; „kein Container erklärt es" ist eine Richtung.

## Don'ts

- **Nicht** die Schwelle so hoch setzen, dass sie nie auslöst, um Ruhe zu haben. Dann lieber keine Regel.
- **Keine** Meldung je Messintervall. Zustandswechsel, nicht Zustände.
- **Nicht** die Baseline über weniger als 48 h anlernen — der Tagesgang braucht mindestens zwei Zyklen.
- **Nicht** die Container-Summe als „das, was läuft" verstehen: kurzlebige Container fehlen darin. Deshalb ist FR-03 ein Hinweis, keine Beweisführung, und muss so formuliert sein.

## Abhängigkeiten

- **Wird blockiert von:** SP-3 (Zeitreihen und Selbstmetriken).
- **Blockiert:** SP-6 (Wirkungskontrolle misst gegen diese Signale).
- **Verwandt:** attackResponse AR-2 (dort dieselbe Mechanik für Log-Muster) — Schwellen-/Fenster-Logik sollte geteilt werden, nicht zweimal gebaut.

## Offene Fragen

- **F-01:** Werden die Daten vom 20.–26.08. noch vorgehalten (Retention 90 Tage ⇒ ja)? Vor Umsetzungsbeginn sichern, sonst geht der Prüfstand verloren.
- **F-02:** z-Score-Schwelle: 3σ ist der Lehrbuchwert, für zackige Serverlast womöglich zu eng. Gegen echte Flottendaten kalibrieren, nicht raten.
