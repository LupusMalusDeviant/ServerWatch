# PRD-0005: Not-Aus (SP-5)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) — die Sofortmaßnahme erforderte SSH auf den betroffenen Server
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-5
- **Ersetzt:** —

## Problem / Motivation

Als am 26.08. klar war, dass Whiskers selbst die Last verursacht, gab es keinen Weg, das in Whiskers abzustellen. Es gibt einen Read-Only-Kill-Switch für den **Agenten** (`ReadOnlyModeRule`), aber keinen für die Hintergrund-Loops. Die Entschärfung lief über SSH auf dem Zielserver, an Whiskers vorbei.

Das ist die falsche Reihenfolge: Wer ein Werkzeug betreibt, das eine Störung verursachen kann, braucht in diesem Werkzeug einen Weg, es sofort zurückzunehmen — ohne Neustart, ohne Konfigurationsdatei, ohne Zugriff auf den betroffenen Server.

Dazu kommt der automatische Fall: Wenn der Circuit Breaker aus SP-1 für einen Server öffnet, sollte nicht nur der auslösende Loop aufhören zu fragen, sondern alle.

## Ziele

- Whiskers lässt sich in Sekunden dazu bringen, einen Server (oder die ganze Flotte) in Ruhe zu lassen.
- Die Rücknahme ist sichtbar, zeitlich begrenzt und wird gemeldet.
- Ein automatisch ausgelöster Not-Aus ist vom manuellen unterscheidbar.

## Non-Goals

- **Kein** Herunterfahren von Whiskers. Die Oberfläche bleibt bedienbar, die Historie lesbar.
- **Keine** Sperre für interaktive Aktionen (Nutzer/Agent/MCP) — nur die Hintergrund-Loops pausieren.
- **Kein** Ersatz für den Circuit Breaker aus SP-1. Der Not-Aus ist die grobe Kelle, der Circuit die feine.
- **Keine** Veränderung auf dem überwachten Server.

## Zielgruppen / Personas

### Flottenbetreiber im Störungsfall

- Kontext: Ein Server steht unter Last, die Ursache ist unklar.
- Pain Point: Braucht eine Möglichkeit, Whiskers als Verursacher **auszuschließen** — schnell und ohne Nebenwirkung.

### Betreiber im Wartungsfenster

- Kontext: Führt Wartung an einem Server durch und will für 30 Minuten Ruhe.
- Pain Point: Bekommt heute Alarme aus einer Situation, die er selbst herbeigeführt hat.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Ein `ILoopSuspensionService` hält den Pausenzustand je Server und global; alle Hintergrund-Loops fragen ihn vor jedem Zyklus ab. | Must |
| FR-02 | Oberfläche: Schalter „Server in Ruhe lassen" je Server und ein globaler Schalter, jeweils mit Dauerauswahl (15 min / 1 h / 4 h / bis Widerruf). | Must |
| FR-03 | Eine Pause läuft automatisch ab; „bis Widerruf" erzeugt eine wiederkehrende Erinnerung (Default täglich). | Must |
| FR-04 | Öffnender Circuit Breaker (SP-1) pausiert automatisch alle Loops für diesen Server; Rückkehr erfolgt automatisch bei geschlossenem Circuit. | Must |
| FR-05 | Jeder Pausenbeginn und jedes Pausenende erzeugt eine Meldung und einen Audit-Eintrag mit Auslöser (Nutzer oder „automatisch"). | Must |
| FR-06 | Pausierte Server sind im Dashboard eindeutig als „pausiert" gekennzeichnet — nicht als gesund und nicht als Ausfall. | Must |
| FR-07 | Der Pausenzustand überlebt einen Neustart von Whiskers. | Must |
| FR-08 | Ein pausierter Server wird beim Ablauf der Pause **ohne** Nachhol-Sturm wieder aufgenommen: kein Aufholen versäumter Zyklen. | Must |
| FR-09 | Der Not-Aus ist über MCP erreichbar (Level `write`), damit ein Mensch ihn per Chat auslösen kann. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** Präzisierung zu FR-09: `get_suspension_status` (read) sowie `suspend_server_loops` / `resume_server_loops` (write), mit Dauer und Pflichtbegründung; der globale Not-Aus bleibt der Oberfläche mit Admin-Recht vorbehalten. | Must |

## Nicht-Funktionale Anforderungen

- **Wirkung < 5 Sekunden:** Ein laufender Zyklus darf zu Ende laufen, ein neuer nicht mehr starten. In Verbindung mit SP-1 endet auch der laufende Aufruf zeitnah.
- **Fail-safe-Richtung:** Fällt der Pausendienst aus, laufen die Loops weiter (Beobachtung ist der Normalzustand) — aber der Ausfall wird gemeldet.
- **Kein Datenverlust:** Metriken- und Alarmhistorie bleiben lesbar; nur die Erhebung pausiert.

## User Stories

- **US-01:** Als Betreiber möchte ich mit einem Klick verhindern, dass Whiskers einen überlasteten Server weiter befragt.
- **US-02:** Als Betreiber möchte ich, dass eine vergessene Pause von selbst endet oder mich erinnert.
- **US-03:** Als Betreiber möchte ich im Dashboard sofort sehen, welche Server gerade nicht überwacht werden.

### Flow für US-01

```
Given ein Server unter Verdacht
When der Betreiber "1 Stunde in Ruhe lassen" wählt
Then startet kein Hintergrund-Loop mehr einen Zyklus für diesen Server,
     das Dashboard zeigt "pausiert bis 15:40",
     eine Meldung dokumentiert wer, wann, wie lange,
     und nach Ablauf läuft der normale Betrieb ohne Nachholen weiter
```

## Akzeptanzkriterien

- FR-01 bis FR-08 umgesetzt.
- Messung: Nach Auslösen des Not-Aus geht innerhalb von 60 Sekunden **kein** neuer Docker-Aufruf mehr an den Server — nachgewiesen im Zugriffslog des Docker-Proxys, nicht in der Whiskers-Oberfläche.
- Nach Ablauf der Pause: die Aufrufrate kehrt auf Normalniveau zurück, ohne Spitze (kein Nachholen).
- Neustart während einer Pause: die Pause gilt weiter.
- MCP: Ein per MCP gesetzter Not-Aus wirkt nachweislich auf dem Zielserver und ist im Audit als agenten-ausgelöst erkennbar.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Wirksamkeit | Zugriffslog des Docker-Proxys auf dem Zielserver | 0 neue Anfragen nach 60 s | weiter Anfragen ⇒ ein Loop fragt den Dienst nicht ab |
| Vollständigkeit | `whiskers_self_calls_total` je Loop (SP-3) | alle Loops auf 0 | ein Loop läuft weiter ⇒ FR-01 nicht flächendeckend verdrahtet |
| Rückkehr ohne Sturm | Aufrufrate nach Pausenende | ≤ Normalniveau | Spitze > 3× ⇒ FR-08 verletzt, die Pause verursacht die nächste Last |
| Sichtbarkeit | Dashboard | „pausiert" mit Restzeit | „gesund" ⇒ der gefährlichste Fehler dieses Pakets |
| Vergessene Pausen | Zahl der Pausen „bis Widerruf" älter als 7 Tage | 0 | > 0 ⇒ blinde Flecken, die niemand mehr kennt |
| Neustartfestigkeit | Neustart im pausierten Zustand | Pause gilt weiter | Loops laufen an ⇒ FR-07 kaputt |

## Woran ich sehe, dass es bricht

1. **Ein pausierter Server, der wie ein gesunder aussieht, ist schlimmer als gar keine Pause.** Das ist der zentrale Ausfallmodus: Whiskers meldet nichts, weil es nichts prüft, und der Betreiber liest „alles in Ordnung". **Betriebsprüfung:** eine eigene, nicht abschaltbare Regel meldet täglich alle Server, die länger als 24 h pausiert sind. Diese Regel darf der Not-Aus nicht pausieren können.
2. **Teilweise Wirkung ist Selbsttäuschung.** Wenn vier von fünf Loops pausieren, sinkt die Last sichtbar — und der fünfte läuft weiter. Das sieht nach Erfolg aus. **Gegenprobe:** die Wirksamkeit wird auf dem **Zielserver** gemessen (Proxy-Zugriffslog), nicht in Whiskers. Nur diese Messung schließt den vergessenen Loop aus.
3. **Der Nachhol-Sturm.** Loops, die versäumte Zyklen aufholen, verursachen beim Pausenende genau die Lastspitze, gegen die die Pause gedacht war. **Messstelle:** Aufrufrate in den ersten fünf Minuten nach Pausenende gegen den Normalwert.
4. **Der Pausendienst als neue Fehlerquelle.** Ein Dienst, den jeder Loop vor jedem Zyklus fragt, ist ein neuer gemeinsamer Ausfallpunkt. Fällt er in die falsche Richtung aus, steht die gesamte Überwachung still — lautlos. **Gegenmaßnahme:** fail-open (Loops laufen), und ein Fehler beim Abfragen des Pausenzustands wird gemeldet, nicht verschluckt.
5. **Automatisch und manuell nicht unterscheidbar.** Wenn eine Circuit-Pause (FR-04) wie ein Nutzerklick aussieht, glaubt der Betreiber, er habe die Pause selbst gesetzt, und sucht nicht nach der Ursache. **Gegenprobe:** Audit-Eintrag und Dashboard nennen den Auslöser immer explizit.

## Do's

- **Auf dem Zielserver messen**, nicht in Whiskers.
- **Jede Pause mit Ablaufdatum** — „bis Widerruf" nur mit Erinnerung.
- **Die Aufsichtsregel aus Punkt 1 zuerst bauen**, bevor der Not-Aus scharf geschaltet wird.
- **Pausiert ≠ gesund ≠ ausgefallen** — drei unterscheidbare Darstellungen.

## Don'ts

- **Nicht** über eine Umgebungsvariable oder Konfigurationsdatei lösen. Ein Not-Aus, der einen Neustart braucht, ist im Ernstfall keiner.
- **Nicht** interaktive Aktionen mitsperren. Wer die Pause setzt, will danach oft gerade die Logs ansehen.
- **Keine** versäumten Zyklen nachholen.
- **Nicht** die Aufsichtsregel aus Punkt 1 durch den Not-Aus pausierbar machen. Sonst lässt sich die Blindheit mit-abschalten.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 (Circuit-Zustand für FR-04; ohne echten Abbruch wirkt die Pause zudem verzögert).
- **Verwandt:** attackResponse AR-3 (`stopped-by-policy`) — beides sind Zustände „bewusst nicht normal"; die Darstellung sollte eine gemeinsame Sprache haben.

## Offene Fragen

- **F-01:** Soll eine Pause auch die Alarm-Auswertung stilllegen oder nur die Erhebung? Vorschlag: nur die Erhebung; bestehende offene Meldungen bleiben sichtbar.
- **F-02:** Globaler Not-Aus — Admin-Recht oder darf jeder mit Schreibrecht? Vorschlag: Admin, weil er die gesamte Flotte blind macht.
