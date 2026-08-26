# PRD-0006: Wirkungskontrolle (SP-6)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte, Freigebende von Agenten-Aktionen
- **Auslöser:** Analyse zum [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) — „eine Automatik, die auf ein Signal hin handelt, das sie selbst erzeugt, ist derselbe Fehler eine Ebene höher"
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-6
- **Ersetzt:** —

## Problem / Motivation

Whiskers handelt bereits automatisch: AI-Trigger starten Agentenläufe, geplante Aufgaben starten Container neu, Auto-Update tauscht Images, und mit SP-1/SP-2/SP-5 kommen Selbstdrosselung, Aussperrung und Not-Aus dazu.

Für keine dieser Aktionen prüft irgendetwas, **ob sie gewirkt hat**. Eine Aktion gilt als erfolgreich, wenn der Aufruf ohne Fehler zurückkommt — nicht, wenn das Problem verschwunden ist. Damit ist „handeln" von „das Richtige tun" nicht unterscheidbar.

Der Vorfall zeigt, warum das gefährlich ist: Der Log-Monitor hat ihn in dem Glauben verursacht, sein 15-Sekunden-Timeout schütze bereits. Die Maßnahme lief, die Rückmeldung fehlte. Je mehr Zähne Whiskers bekommt, desto teurer wird diese Lücke — eine Automatik ohne Regelkreis ist nur eine schnellere Art, falsch zu liegen.

## Ziele

- Jede automatische Aktion hat ein vorher benanntes Erfolgskriterium und wird daran gemessen.
- Bleibt die Wirkung aus, wird die Aktion zurückgenommen oder eskaliert — nicht wiederholt.
- Wiederholt wirkungslose Aktionen sind erkennbar, statt sich zu stapeln.

## Non-Goals

- **Keine** Wirkungskontrolle für rein interaktive Aktionen, die ein Mensch gerade beobachtet.
- **Keine** automatische Rücknahme von Aktionen an fremden Systemen ohne Freigabe — dort nur Meldung und Vorschlag.
- **Keine** neue Aktionsart. Dieses Paket bewertet vorhandene Aktionen.
- **Kein** allgemeines Workflow-/Saga-Framework.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Sieht in der Historie „Container neu gestartet — Erfolg", weiß aber nicht, ob es geholfen hat.

### Freigebender

- Kontext: Bestätigt eine Agenten-Aktion.
- Pain Point: Trägt die Verantwortung für eine Aktion, deren Wirkung niemand nachhält.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Jede automatisierbare Aktionsart trägt ein deklariertes Erfolgskriterium: Metrik, Zielrichtung, Prüffenster (z. B. „Host-CPU < 50 % innerhalb 10 min"). | Must |
| FR-02 | Nach jeder automatischen Aktion wird nach Ablauf des Prüffensters gemessen und das Ergebnis (`gewirkt` / `nicht gewirkt` / `nicht messbar`) am Aktionseintrag festgehalten. | Must |
| FR-03 | Aktionen, die Whiskers **an sich selbst** vornimmt (Drosselung, Aussperrung, Not-Aus), werden bei ausbleibender Wirkung automatisch zurückgenommen und eskaliert. | Must |
| FR-04 | Aktionen an **fremden Systemen** werden bei ausbleibender Wirkung nicht automatisch zurückgenommen, sondern gemeldet — mit dem Rücknahmevorschlag als Ein-Klick-Angebot. | Must |
| FR-05 | Zwei wirkungslose Versuche derselben Aktion auf demselben Ziel sperren weitere automatische Versuche und erzeugen eine Eskalation. | Must |
| FR-06 | Das Ergebnis ist über `CorrelationId` (WP-05) mit Auslöser, Freigabe und Aktion verkettet. | Must |
| FR-07 | Eine Übersicht „Aktionen und ihre Wirkung" zeigt Trefferquote je Aktionsart über die Zeit. | Should |
| FR-08 | `nicht messbar` ist ein erstklassiges Ergebnis und wird als solches gezählt, nicht als Erfolg. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_action_outcomes` (read): Ergebnis und Trefferquote je Aktionsart und Ziel. Der Agent muss sehen können, ob seine **eigene** vorherige Aktion gewirkt hat — sonst wiederholt er sie. | Must |

## Nicht-Funktionale Anforderungen

- **Kein Dauerzustand:** Ein Prüffenster endet garantiert; hängende Prüfungen dürfen sich nicht ansammeln.
- **Neustartfest:** Ein Neustart während eines offenen Prüffensters führt zu `nicht messbar`, nicht zu stillem Verschwinden.
- **Kein Aufruf-Overhead:** Die Prüfung liest vorhandene Zeitreihen, sie erhebt keine neuen Daten.

## User Stories

- **US-01:** Als Betreiber möchte ich in der Historie sehen, ob eine Aktion das Problem gelöst hat.
- **US-02:** Als Betreiber möchte ich, dass eine wirkungslose Selbstdrosselung sich selbst zurücknimmt, statt den Blindflug zu verlängern.
- **US-03:** Als Freigebender möchte ich erkennen, dass eine Aktionsart bei uns regelmäßig nichts bringt, bevor ich sie das zehnte Mal freigebe.

### Flow für US-02

```
Given Whiskers hat wegen Timeouts einen Container ausgesperrt
When nach 15 Minuten die Host-CPU unverändert bei 95 % steht
Then wird die Aussperrung aufgehoben (sie war nicht die Ursache),
     das Ergebnis "nicht gewirkt" am Eintrag vermerkt,
     und eine Eskalation nennt: "Selbstdrosselung ohne Wirkung — Ursache liegt woanders"
```

## Akzeptanzkriterien

- FR-01 bis FR-06 und FR-08 umgesetzt.
- Für mindestens diese Aktionsarten existiert ein Erfolgskriterium: Container-Neustart, Auto-Update, Selbstdrosselung, Aussperrung, Not-Aus, Agenten-Aktion mit Schreibwirkung.
- Nachweis im Negativfall: eine absichtlich wirkungslose Aktion (Neustart eines Containers, dessen Problem außerhalb liegt) wird nach dem Prüffenster als `nicht gewirkt` geführt und beim zweiten Versuch gesperrt.
- Nachweis für `nicht messbar`: fällt die Metrikquelle während des Fensters aus, steht `nicht messbar` — nicht `gewirkt`.
- MCP: Nach einer Agenten-Aktion liefert `get_action_outcomes` deren Ergebnis inklusive `nicht messbar`, verkettet über die `CorrelationId` des Laufs.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Verteilung der Ergebnisse | Übersicht FR-07 | gemischt, mit erkennbarem `nicht gewirkt`-Anteil | **100 % `gewirkt`** ⇒ das Kriterium ist zu weich formuliert, nicht die Automatik perfekt |
| Anteil `nicht messbar` | Übersicht FR-07 | < 10 % | > 30 % ⇒ die Kriterien messen gegen Daten, die es oft nicht gibt |
| Offene Prüffenster | Zähler | nahe 0, kurzlebig | wachsend ⇒ Fenster schließen nicht, Prüfungen versanden |
| Gesperrte Aktionsarten (FR-05) | Übersicht | selten, mit Eskalation | häufig ohne Eskalation ⇒ Sperre wirkt, aber niemand erfährt es |
| Trefferquote je Aktionsart über 30 Tage | Übersicht FR-07 | stabil oder steigend | sinkend ⇒ die Aktion passt nicht mehr zur Realität der Flotte |
| Verkettung | Stichprobe: von der Meldung zur Aktion zum Ergebnis | lückenlos über `CorrelationId` | Bruch ⇒ FR-06 nicht durchgezogen, Nachvollzug unmöglich |

## Woran ich sehe, dass es bricht

1. **Eine Trefferquote von 100 % ist ein Alarm, kein Erfolg.** Wenn jede Aktion als wirksam gilt, ist das Erfolgskriterium so weit gefasst, dass es nichts ausschließt — der wahrscheinlichste Zustand nach der Umsetzung. **Gegenprobe, die verpflichtend ist:** eine absichtlich wirkungslose Aktion muss als `nicht gewirkt` erkannt werden. Ohne diesen Nachweis ist die gesamte Übersicht wertlos.
2. **`nicht messbar` verkleidet als Erfolg.** Der bequemste Implementierungsfehler: fehlende Daten werden als „kein Problem mehr" gelesen. Damit meldet das System Erfolg, wenn die Messung ausfällt — exakt das Muster des ursprünglichen Vorfalls. **Messstelle:** Anteil `nicht messbar` muss überhaupt größer null sein; ist er konstant 0, wird er nicht erhoben.
3. **Rücknahme, die selbst schadet.** Eine automatische Rücknahme (FR-03) kann Flattern erzeugen: drosseln → zurücknehmen → drosseln. **Messstelle:** Zahl der Zustandswechsel je Server und Stunde; mehr als drei ⇒ Flattern, Prüffenster zu kurz oder Kriterium zu eng.
4. **Prüffenster, die nie enden.** Ein Neustart mitten im Fenster darf keine Karteileiche erzeugen. **Messstelle:** offene Prüffenster mit Startzeit älter als das doppelte Fenster; jeder solche Eintrag ist ein Fehler.
5. **Verantwortung, die sich verflüchtigt.** Wenn die Kette Auslöser → Aktion → Ergebnis reißt, lässt sich nach einem Vorfall nicht mehr sagen, wer was ausgelöst hat. **Gegenprobe:** ein Stichprobenlauf, der zu fünf zufälligen Aktionen die vollständige Kette rekonstruiert.

## Do's

- **Das Erfolgskriterium zusammen mit der Aktion definieren**, nicht nachträglich. Eine Aktion ohne Kriterium darf nicht automatisch laufen dürfen.
- **Bei eigenen Maßnahmen zurücknehmen, bei fremden melden.** Diese Trennlinie ist die gleiche wie in `attackResponse.md` und im Vorfallsbericht.
- **Zweiter wirkungsloser Versuch = Stopp.** Nicht der dritte, nicht der zehnte.
- **`nicht messbar` sichtbar machen**, damit fehlende Datenquellen auffallen.

## Don'ts

- **Nicht** den Rückgabewert des Aufrufs als Wirkung werten. „Der Neustart lief" ist keine Aussage über das Problem.
- **Keine** automatische Rücknahme auf fremden Systemen. Ein zurückgenommener Container-Neustart ist ein zweiter Ausfall.
- **Nicht** das Prüffenster so lang wählen, dass die Störung ohnehin von selbst vergeht — dann misst man Zeit, nicht Wirkung.
- **Nicht** bei ausbleibender Wirkung dieselbe Aktion wiederholen. Das ist die Ratsche aus dem Vorfall in neuer Form.

## Abhängigkeiten

- **Wird blockiert von:** SP-4 (die Signale, gegen die gemessen wird), SP-1/SP-2/SP-5 (die Aktionen, die geprüft werden).
- **Verwandt:** attackResponse AR-5/AR-6 — dort dieselbe Mechanik für Schutzmaßnahmen; ein Modell, nicht zwei.

## Offene Fragen

- **F-01:** Prüffenster je Aktionsart oder global? Vorschlag: je Aktionsart, weil ein Auto-Update anders wirkt als eine Drosselung.
- **F-02:** Soll eine gesperrte Aktionsart (FR-05) nach Zeitablauf automatisch wieder freigegeben werden? Vorschlag: nein — Entsperrung ist eine bewusste Entscheidung, wie in AR-6.
