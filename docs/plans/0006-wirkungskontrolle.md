# Plan-0006: Wirkungskontrolle (SP-6)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0006](../prd/0006-wirkungskontrolle.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Whiskers handelt automatisch, und keine dieser Aktionen wird auf ihre Wirkung geprüft. Eine Aktion gilt als erfolgreich, wenn der Aufruf ohne Fehler zurückkam — nicht, wenn das Problem verschwunden ist.

Mit SP-1, SP-2 und SP-5 kommen drei weitere automatische Eingriffe hinzu. Ohne Regelkreis vervielfacht sich damit das Risiko, das der Vorfall gezeigt hat: eine Maßnahme läuft, die Rückmeldung fehlt, und der Glaube an die Wirkung ersetzt die Prüfung.

## Ziele

- Jede automatische Aktion hat ein vorher benanntes Erfolgskriterium.
- Eigene Maßnahmen nehmen sich bei ausbleibender Wirkung zurück, fremde werden gemeldet.
- Wirkungslose Aktionen wiederholen sich nicht.

## Arbeitspakete

### WP1: Erfolgskriterien deklarieren

**Zweck:** Ohne vorher benanntes Kriterium ist jede spätere Bewertung Auslegung.
**Schätzung:** M (1,5 Tage).

1. **WP1.1:** Modell `ActionOutcomeCriterion`: Metrik, Zielrichtung, Schwellwert, Prüffenster.
2. **WP1.2:** Kriterien für die vorhandenen Aktionsarten festlegen: Container-Neustart, Auto-Update, Selbstdrosselung, Aussperrung, Not-Aus, Agenten-Aktion mit Schreibwirkung.
3. **WP1.3:** Regel im Code verankern: **Eine Aktionsart ohne Kriterium darf nicht automatisch ausgeführt werden.** Als Startprüfung oder Architekturtest, nicht als Konvention.

**Ergebnis:** Jede Automatik hat ein Versprechen, an dem sie gemessen wird.

**Abnahme:** Eine neu registrierte Aktionsart ohne Kriterium bricht den Testlauf.

### WP2: Prüffenster

**Zweck:** Nach der Aktion messen, verlässlich und begrenzt.
**Schätzung:** M (2 Tage).

1. **WP2.1:** Persistenter Auftrag je ausgeführter Aktion mit Fälligkeitszeitpunkt — Neustart-fest, damit kein Fenster verschwindet.
2. **WP2.2:** Auswertung liest **vorhandene** Zeitreihen (SP-3/SP-4), erhebt nichts Neues.
3. **WP2.3:** Ergebnis `gewirkt` / `nicht gewirkt` / `nicht messbar` am Aktionseintrag, verkettet über `CorrelationId`.
4. **WP2.4:** `nicht messbar` bei fehlenden Daten oder Neustart im Fenster — **niemals** als Erfolg werten. Das ist die zentrale Implementierungsregel dieses Plans.

**Abnahme:** Metrikquelle während eines Fensters abklemmen ⇒ Ergebnis `nicht messbar`, nicht `gewirkt`.

### WP3: Rücknahme und Meldung

**Zweck:** Die Trennlinie zwischen eigenen und fremden Systemen einhalten.
**Schätzung:** M (1,5 Tage).

1. **WP3.1:** Eigene Maßnahmen (Drosselung, Aussperrung, Not-Aus) bei `nicht gewirkt` automatisch zurücknehmen und eskalieren — mit dem Text, dass die Ursache woanders liegt.
2. **WP3.2:** Fremdsystem-Aktionen bei `nicht gewirkt` **nur melden**, mit dem Rücknahmevorschlag als Ein-Klick-Angebot.
3. **WP3.3:** Flatterschutz: Mindestabstand zwischen Rücknahme und erneuter gleichartiger Maßnahme.

**Abnahme:** Eine wirkungslose Aussperrung wird aufgehoben; ein wirkungsloser Container-Neustart wird **nicht** rückgängig gemacht, sondern gemeldet.

### WP4: Wiederholungssperre

**Zweck:** Die Ratsche aus dem Vorfall in neuer Form verhindern.
**Schätzung:** S (1 Tag).

1. **WP4.1:** Zwei wirkungslose Versuche derselben Aktionsart auf demselben Ziel sperren weitere automatische Versuche.
2. **WP4.2:** Die Sperre erzeugt eine Eskalation — nicht Stille.
3. **WP4.3:** Entsperrung ist eine bewusste Handlung, kein Zeitablauf (gleiche Haltung wie AR-6).

**Abnahme:** Dritter automatischer Versuch findet nicht statt; die Eskalation ist zugestellt.

### WP5: Übersicht

**Zweck:** Die Trefferquote sichtbar machen — der eigentliche Nutzen des Pakets.
**Schätzung:** M (1,5 Tage).

1. **WP5.1:** Ansicht „Aktionen und ihre Wirkung": je Aktionsart Trefferquote über die Zeit, Anteil `nicht messbar`, gesperrte Ziele.
2. **WP5.2:** Von jeder Zeile aus die vollständige Kette Auslöser → Aktion → Ergebnis erreichbar.
3. **WP5.3:** Kennzahl „offene Prüffenster" mit Warnung bei Überalterung.

**Abnahme:** Zu fünf zufälligen Aktionen lässt sich die vollständige Kette rekonstruieren.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_action_outcomes` — Wirkung und Trefferquote je Aktionsart und Ziel. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Der Agent liest das Ergebnis seiner eigenen vorherigen Aktion, inklusive `nicht messbar`. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
SP-4 (Signale) ──> WP1 ──> WP2 ──> WP3 ──> WP4
SP-1/2/5 (Aktionen) ─┘              └────> WP5
```

- **Extern blockiert von:** SP-4 (die Metriken, gegen die gemessen wird), SP-1/SP-2/SP-5 (die Aktionen).
- **Gemeinsam bauen mit:** attackResponse AR-5/AR-6 — ein Modell für Wirkungskontrolle, nicht zwei.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Ergebnisverteilung | Ansicht WP5.1 | gemischt, mit `nicht gewirkt`-Anteil |
| Anteil `nicht messbar` | Ansicht WP5.1 | > 0 und < 10 % |
| Offene Prüffenster | Kennzahl WP5.3 | kurzlebig, nicht wachsend |
| Zustandswechsel je Server und Stunde | Selbstmetriken | ≤ 3 (sonst Flattern) |
| Gesperrte Aktionsarten | Ansicht WP5.1 | jede hat eine Eskalation |
| Kettenintegrität | Stichprobe über `CorrelationId` | lückenlos |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Trefferquote 100 % | Kriterium zu weich, Übersicht wertlos | verpflichtende Gegenprobe mit absichtlich wirkungsloser Aktion |
| `nicht messbar` als Erfolg gelesen | meldet Erfolg, wenn die Messung ausfällt — das Muster des Vorfalls | WP2.4 + Kennzahl „Anteil `nicht messbar` > 0" |
| Flattern durch Rücknahme | drosseln → zurücknehmen → drosseln | WP3.3 + Kennzahl Zustandswechsel |
| Karteileichen bei Neustart | Prüfungen versanden | WP2.1 persistent, WP5.3 überwacht |
| Prüffenster zu lang | misst Zeit statt Wirkung | Fenster je Aktionsart, kalibriert an der typischen Wirkdauer |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 | Aktionsart ohne Kriterium bricht den Testlauf |
| M2 | WP2 | absichtlich wirkungslose Aktion ⇒ `nicht gewirkt`; Quelle abgeklemmt ⇒ `nicht messbar` |
| M3 | WP3 | eigene Maßnahme zurückgenommen, fremde nur gemeldet |
| M4 | WP4 + WP5 | dritter Versuch unterbleibt, Kette über fünf Stichproben vollständig |
| M5 | Feldlauf | 30 Tage; Trefferquoten je Aktionsart dokumentiert und bewertet |

## Rückweg

Rein beobachtend startbar: WP1/WP2 lassen sich ohne WP3/WP4 betreiben — dann wird nur bewertet, nicht eingegriffen. Das ist der empfohlene Einstieg: erst vier Wochen messen, dann die Rücknahme scharf schalten. Damit ist auch belegt, ob die Kriterien taugen, bevor sie Wirkung entfalten.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [ ] Gegenprobe mit absichtlich wirkungsloser Aktion ⇒ `nicht gewirkt`
- [ ] Gegenprobe mit abgeklemmter Metrikquelle ⇒ `nicht messbar`
- [ ] Eigene Maßnahmen werden zurückgenommen, fremde ausschließlich gemeldet
- [ ] Dritter automatischer Versuch findet nicht statt, Eskalation zugestellt
- [ ] Kette Auslöser → Aktion → Ergebnis über fünf Stichproben lückenlos
- [ ] Vier Wochen Beobachtungsbetrieb vor dem Scharfschalten der Rücknahme
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
