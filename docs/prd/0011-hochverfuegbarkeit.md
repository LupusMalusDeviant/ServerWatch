# PRD-0011: Hochverfügbarkeit (GAP-4)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber, für die Whiskers eine betriebskritische Kontrollebene ist
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — GAP-4
- **Ersetzt:** —

## Problem / Motivation

Whiskers läuft mit genau einer Instanz. Das ist eine bewusste, dokumentierte Entscheidung (`kubernetesImplement.md` A.0: `replicas: 1`, `strategy: Recreate`) und hat vier konkrete Gründe:

1. **Blazor Server** hält zustandsbehaftete SignalR-Verbindungen je Nutzer.
2. **Acht und mehr Hintergrund-Loops** laufen ohne Leader-Election — bei zwei Instanzen liefe jeder Loop doppelt.
3. **JSON-Dateispeicher** mit Prozess-Zwischenspeicher (`servers.json`, `roles.json`, `vault.json`) haben kein Sperrmodell für parallele Schreiber.
4. **SQLite** als Standard-Datenbank kennt keinen zweiten Schreiber über das Netz.

Für die Zielgruppe ist das vertretbar und ehrlich dokumentiert. Es hat aber zwei Kosten: In jedem Vergleich mit einer Unternehmenslösung ist es ein K.-o.-Punkt, und praktisch bedeutet ein Neustart von Whiskers ein Fenster, in dem niemand die Flotte sieht — ausgerechnet während eines Updates.

Zwei der vier Gründe sind inzwischen entschärft: PostgreSQL ist verfügbar (`stableDB.md`, erledigt), und der Weg der JSON-Speicher in die Datenbank ist als `changeme.md` C7 beschrieben.

## Ziele

- Ein Update oder ein Ausfall einer Instanz führt nicht dazu, dass die Flotte unbeobachtet bleibt.
- Kein Hintergrund-Loop läuft doppelt.
- Der Zugewinn an Verfügbarkeit kostet weniger Betriebssicherheit, als er bringt.

## Non-Goals

- **Keine** aktiv-aktiv-Skalierung für Durchsatz. Es geht um Verfügbarkeit, nicht um Last.
- **Keine** geografische Verteilung oder Mehr-Rechenzentrums-Auslegung.
- **Keine** Unterstützung für HA mit SQLite. PostgreSQL ist Voraussetzung, ohne Ausnahme.
- **Keine** Abschaffung des Einzelinstanz-Betriebs. Er bleibt der Standard für kleine Installationen.

## Zielgruppen / Personas

### Betreiber mit Betriebspflicht

- Kontext: Whiskers ist die Kontrollebene, über die im Störungsfall gehandelt wird.
- Pain Point: Genau dann darf sie nicht selbst weg sein.

### Betreiber im Vergleichstest gegen Unternehmenslösungen

- Pain Point: „Single Replica by design" beendet die Prüfung, unabhängig von allen anderen Eigenschaften.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Leader-Election über die Datenbank (Sperrzeile mit Pacht und Erneuerung); nur der Leader führt Hintergrund-Loops aus. | Must |
| FR-02 | Jede Instanz zeigt in der Oberfläche an, ob sie Leader ist und seit wann. | Must |
| FR-03 | Verliert der Leader die Pacht, stellt er alle Loops innerhalb eines Erneuerungsintervalls ein — nachweisbar, nicht angenommen. | Must |
| FR-04 | Alle JSON-Dateispeicher sind in die Datenbank überführt (`changeme.md` C7) oder ausdrücklich als Leader-only markiert. | Must |
| FR-05 | Blazor-Sitzungen überstehen den Ausfall einer Instanz durch klebende Sitzungen plus sauberes Wiederverbinden; im Zweifel neu anmelden statt stiller Fehlfunktion. | Must |
| FR-06 | PostgreSQL ist für den Mehrinstanzbetrieb Pflicht; SQLite verweigert den Start bei `replicas > 1` mit klarer Meldung. | Must |
| FR-07 | Das Helm-Chart unterstützt `replicas > 1` mit `RollingUpdate`, PodDisruptionBudget und passenden Proben. | Must |
| FR-08 | Ein Update-Ablauf ohne Beobachtungslücke: die neue Instanz übernimmt die Leaderschaft erst nach bestandener Bereitschaftsprüfung. | Must |
| FR-09 | Selbstmetriken (SP-3) je Instanz getrennt, mit Instanzkennung. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_cluster_role` (read): Leaderschaft, Instanzkennung und Pachtalter je Instanz. Ohne dieses Werkzeug kann der Agent bei „es passiert nichts mehr“ die Leader-Summe nicht prüfen. | Must |

## Nicht-Funktionale Anforderungen

- **Split-Brain ist unzulässig:** Zwei Leader gleichzeitig sind ein Datenintegritätsproblem — doppelte Aktionen, doppelte Alarme, konkurrierende Schreibzugriffe auf die Flotte. Die Pachtmechanik muss eher blockieren als doppelt laufen.
- **Kein Verlust an Einfachheit für Kleininstallationen:** Der Einzelinstanzbetrieb bleibt ohne Zusatzkonfiguration lauffähig.
- **Beobachtbarkeit zuerst:** Ohne SP-3 ist ein Mehrinstanzbetrieb nicht diagnostizierbar; das ist eine harte Voraussetzung, keine Empfehlung.

## User Stories

- **US-01:** Als Betreiber möchte ich Whiskers aktualisieren, ohne dass die Flotte unbeobachtet bleibt.
- **US-02:** Als Betreiber möchte ich sehen, welche Instanz gerade die Loops fährt.
- **US-03:** Als Betreiber möchte ich sicher sein, dass ein Netzsplit keine doppelten Aktionen auslöst.

### Flow für US-01

```
Given zwei Instanzen, Instanz A ist Leader
When ein Rolling Update Instanz B ersetzt
Then bleibt A Leader, B wird bereit, A wird ersetzt,
     B übernimmt die Pacht, und die Lücke in der Loop-Ausführung
     ist kürzer als ein Zyklusintervall — messbar an last_success_timestamp
```

## Akzeptanzkriterien

- FR-01 bis FR-09 umgesetzt.
- Nachweis Split-Brain-Freiheit: Bei künstlich unterbrochener Datenbankverbindung des Leaders stellt dieser die Loops ein, **bevor** ein zweiter Leader die Pacht übernimmt. Zeitliche Überlappung = 0, gemessen über die Selbstmetriken beider Instanzen.
- Rolling Update erzeugt eine Lücke von weniger als einem Zyklusintervall in `last_success_timestamp`.
- Doppelte Aktionen ausgeschlossen: In einem 24-Stunden-Lauf mit zwei Instanzen existiert kein Alarm und keine geplante Aufgabe doppelt — geprüft über die Alarm-Historie.
- Start mit SQLite und `replicas: 2` schlägt mit einer verständlichen Meldung fehl, statt Daten zu beschädigen.
- MCP: Der Agent erkennt allein über `get_cluster_role` eine Leader-Summe von 0 oder 2 und meldet sie als Störung.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Leader-Eindeutigkeit | `whiskers_self_is_leader` über alle Instanzen | Summe konstant 1 | Summe 2 ⇒ **Split-Brain, sofortiger Stopp des Vorhabens** |
| Summe 0 | dieselbe Kennzahl | nur kurz beim Wechsel | dauerhaft 0 ⇒ niemand fährt die Loops, alles sieht ruhig aus |
| Doppelte Alarme | Alarm-Historie auf Duplikate je Dedupe-Merkmal | keine | Duplikate ⇒ Leader-Prüfung greift nicht in allen Loops |
| Lücke beim Update | `last_success_timestamp` je Loop über den Update-Zeitraum | < 1 Intervall | größer ⇒ FR-08 nicht wirksam |
| Sitzungsverhalten | Instanz während aktiver Nutzung beenden | Wiederverbindung oder klare Neuanmeldung | eingefrorene Oberfläche ⇒ FR-05 verfehlt |
| Pachterneuerung | Datenbanklast durch die Sperrzeile | vernachlässigbar | messbar ⇒ Erneuerungsintervall zu kurz |

## Woran ich sehe, dass es bricht

1. **Split-Brain ist die einzige wirklich gefährliche Fehlerart hier — und sie ist im Normalbetrieb unsichtbar.** Zwei Leader erzeugen doppelte Alarme (fällt auf) und doppelte *Aktionen* (fällt nicht auf, bis eine Aktion zweimal ausgeführt wurde: zwei Neustarts, zwei Updates, zwei Selbstdrosselungen). **Messstelle, die dauerhaft laufen muss:** die Summe von `whiskers_self_is_leader` über alle Instanzen. Sie muss konstant 1 sein; jede Abweichung ist ein Alarm höchster Stufe, kein Diagnosewert.
2. **Die Summe 0 ist genauso schlimm und noch leiser.** Wenn kein Leader existiert, laufen keine Loops — und die Oberfläche ist erreichbar, die Historie sieht normal aus, es kommen einfach keine neuen Alarme. Das ist der Vorfall vom 26.08. in seiner reinen Form: alles ruhig, weil niemand hinsieht. **Messstelle:** dieselbe Summe, plus das Alter von `last_success_timestamp`.
3. **Die Loop-Prüfung, die einer vergisst.** Es genügt ein Hintergrund-Loop, der die Leaderschaft nicht abfragt, und die Doppelausführung ist zurück — nur seltener und dadurch schwerer zu finden. **Gegenprobe:** ein Architektur-Test, der fehlschlägt, sobald ein `BackgroundService` ohne Leader-Prüfung existiert. Dieselbe Bauart wie der Budget-Test in SP-1.
4. **Zustand, der noch im Prozess lebt.** Jeder verbliebene In-Memory-Zwischenspeicher (Cooldowns, Wasserzeichen, Health-Zustände) driftet zwischen den Instanzen auseinander und erzeugt beim Leaderwechsel Sprünge — etwa erneute Alarme für längst bekannte Zustände. **Messstelle:** Alarmrate in den ersten fünf Minuten nach einem Leaderwechsel gegen den Normalwert.
5. **Verfügbarkeit gewonnen, Betriebssicherheit verloren.** Zwei Instanzen mit halbfertiger Koordination sind unzuverlässiger als eine saubere. **Ehrliches Abbruchkriterium:** Bleibt die Summe der Leader über eine Woche Dauerbetrieb nicht konstant 1, wird das Vorhaben zurückgestellt statt nachgebessert.

## Do's

- **`changeme.md` C7 (Speicher in die Datenbank) zuerst.** Ohne das ist FR-04 unerfüllbar und alles Weitere Makulatur.
- **Leader-Prüfung an einer zentralen Stelle** erzwingen, nicht in jedem Loop einzeln.
- **Die Leader-Summe als Dauerkennzahl** führen, ab dem ersten Tag.
- **PostgreSQL zur harten Voraussetzung machen** und den Start bei SQLite verweigern.

## Don'ts

- **Nicht** ohne SP-3 beginnen. Ein Mehrinstanzbetrieb ohne Selbstbeobachtung ist nicht diagnostizierbar.
- **Nicht** ohne PostgreSQL zulassen, auch nicht „zum Ausprobieren".
- **Nicht** klebende Sitzungen als Ersatz für sauberes Wiederverbinden verwenden.
- **Keine** Pacht ohne Ablauf. Ein Leader, der abstürzt, muss von selbst ersetzt werden.
- **Nicht** den Einzelinstanzbetrieb komplizierter machen, um HA zu ermöglichen.

## Abhängigkeiten

- **Wird blockiert von:** `stableDB.md` (PostgreSQL, erledigt), `changeme.md` C7 (Speicher in die Datenbank, offen), SP-3 (Selbstbeobachtung).
- **Nachgelagert:** alles andere in dieser Roadmap. GAP-4 ist letzte Welle.

## Offene Fragen

- **F-01:** Ist HA für die Zielgruppe überhaupt gefragt, oder genügt ein Update ohne Beobachtungslücke? Wenn Letzteres: FR-08 allein ist deutlich billiger als das ganze Paket und liefert den größten Teil des Nutzens.
- **F-02:** Pachtdauer und Erneuerungsintervall — Vorschlag 30 s Pacht, 10 s Erneuerung. Gegen die tatsächliche Datenbanklatenz kalibrieren.
