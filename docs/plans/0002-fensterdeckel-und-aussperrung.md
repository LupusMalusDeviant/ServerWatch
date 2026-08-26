# Plan-0002: Fensterdeckel & Aussperrung (SP-2)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0002](../prd/0002-fensterdeckel-und-aussperrung.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Zweite und dritte Ursache des Vorfalls: das `since`-Fenster wächst bei jedem Fehlschlag um ein Zyklusintervall, und ein dauerhaft unlesbarer Container wird unbegrenzt weiter befragt, ohne dass jemand davon erfährt.

Dieser Plan setzt **zwingend** auf Plan-0001 auf. Ohne echten Abbruch entlastet eine Aussperrung nichts — die alten Anfragen laufen weiter.

## Ziele

- Ein Fehlschlag macht den nächsten Versuch nicht teurer.
- Ein unlesbarer Container wird nach drei Versuchen in Ruhe gelassen und gemeldet.
- Der Zeitraum von der Störung bis zur Meldung liegt unter vier Minuten.

## Arbeitspakete

### WP1: Fensterdeckel

**Zweck:** Der Ratsche die Zähne ziehen.
**Schätzung:** S (0,5 Tage).

1. **WP1.1:** In `LogMonitorService.ScanServerAsync` (Z. ~200) `since` deckeln: `since = Max(letzterErfolg, now - MaxLookback)`, `MaxLookback` Default 10 min.
2. **WP1.2:** Wasserzeichen auch im Fehlerfall setzen — den `catch`-Zweig entsprechend ändern und den irreführenden Kommentar („the next cycle retries the same window") korrigieren.
3. **WP1.3:** Beim Sprung über eine Lücke die übersprungene Zeitspanne festhalten, damit WP4 sie benennen kann.

**Ergebnis:** Die angeforderte Fensterbreite ist nach oben begrenzt.

**Abnahme:** Im Zugriffslog des Docker-Proxys überschreitet der `since`-Parameter über 20 Zyklen mit dauerhaft fehlschlagendem Container nie `MaxLookback`.

### WP2: Fehlschlagzähler je Container

**Zweck:** Die Datengrundlage für Aussperrung und für das früheste Signal überhaupt.
**Schätzung:** S (0,5 Tage).

1. **WP2.1:** Zähler je `{serverId}:{containerId}`, **getrennt nach Fehlerart**: Timeout, Verbindungsfehler, Container nicht vorhanden.
2. **WP2.2:** Rücksetzen bei Erfolg; Aufräumen für entfernte Container, damit die Struktur nicht wächst.
3. **WP2.3:** Zähler an die Selbstmetriken-Schnittstelle melden (SP-3 exportiert sie später).

**Ergebnis:** Die Warnung, die heute im Protokoll versickert, ist eine Zahl.

**Abnahme:** Ein entfernter Container erhöht **nicht** den Timeout-Zähler — Fehlerarten sind sauber getrennt.

### WP3: Aussperrung mit Backoff

**Zweck:** Aufhören, wo Weitermachen nur schadet.
**Schätzung:** S (1 Tag).

1. **WP3.1:** Ab 3 Timeouts in Folge Container aus dem Scan nehmen; Backoff 5 → 15 → 60 min, Deckel 60 min.
2. **WP3.2:** Rückkehr nach Ablauf; ein Erfolg setzt Zähler und Backoff zurück.
3. **WP3.3:** Aussperrung gilt je (Server, Container) — nie für einen ganzen Server (dafür ist der Circuit aus Plan-0001 zuständig).

**Ergebnis:** Ein kaputter Container erzeugt konstante, geringe Last statt wachsender.

**Abnahme:** Test mit simulierter Erholung: der Container wird im nächsten Zyklus wieder gescannt, samt Entwarnung.

### WP4: Meldungen

**Zweck:** Die Regel „jede Selbstdrosselung wird gemeldet" einlösen.
**Schätzung:** S (0,5 Tage).

1. **WP4.1:** Meldung bei Aussperrung: Server, Container, Fehlschlagzahl, Dauer, übersprungener Zeitraum aus WP1.3.
2. **WP4.2:** Meldung bei Rückkehr (Entwarnung).
3. **WP4.3:** Genau eine Meldung je Zustandswechsel — nicht je Zyklus.

**Ergebnis:** Der Betreiber erfährt binnen Minuten von einem blinden Fleck.

**Abnahme:** 20 Zyklen mit dauerhaft kaputtem Container erzeugen genau eine Aussperrungsmeldung.

> 🟢 **WP1–WP4 erledigt** (2026-08-26). Fensterdeckel (`MaxLookback` = 10 min), Wasserzeichen wird **auch im Fehlerfall** fortgeschrieben, Timeout-Zähler je Container **getrennt von anderen Fehlerarten**, Aussperrung nach 3 Timeouts in Folge mit Backoff 5/15/60 min, Meldungen `log_scan_suspended` / `log_scan_resumed` (im `NotificationFormatter` mit eigenem Text und Link auf `/logs`). Meldungen werden auf dem Zyklus-Thread verschickt, vor den Alarmen — „dieser Container wird nicht mehr gelesen" ist Kontext für alles, was danach kommt. **656/656 Tests grün.**
>
> ⚠️ **Zwei Testentwürfe haben in Folge nicht gemessen, was sie behaupteten** — beide nur aufgefallen, weil die Regel „erst rot sehen" eingehalten wurde:
>
> 1. **Zu kleiner Zeitabstand.** Mit 120 ms zwischen den Zyklen wächst das Fenster pro Runde nur um 120 ms und verschwindet in der Toleranz. Der Test war gegen den **unbehobenen** Code grün. Behoben mit 400 ms Abstand und einer Zusicherung auf die Streuung statt auf Erst/Letzt-Differenz.
> 2. **Falsche Ausgangslage.** Ein Container, der **nie** erfolgreich war, hat gar kein Wasserzeichen — `since` fällt jeden Zyklus auf „jetzt" zurück, und das Fenster bleibt flach, ob die Ratsche da ist oder nicht. Der Vorfall hatte die Form „erst gesund, dann langsam"; erst mit einem erfolgreichen ersten Zyklus wird die Ratsche überhaupt sichtbar. Danach unterscheidet der Test sauber: mit Behebung `[414, 411, 409]`, ohne `[414, 911, 1379]`.
>
> Dazu ein dritter, kleinerer Fund: `FakeDocker.Calls` war ein `ConcurrentBag`, dessen Aufzählung nicht der Einfügereihenfolge folgt — `Last()` lieferte den **ersten** Aufruf. Auf `ConcurrentQueue` umgestellt und ein `CallsInOrder`-Zugriff ergänzt.
>
> **Offen aus SP-2:** WP5 (Sichtbarkeit in der Oberfläche und die nicht abschaltbare Aufsichtsregel „letzter erfolgreicher Scan älter als 3 Intervalle"). WP5.3 ist der wichtigere Teil, weil er auch greift, wenn der Meldeweg versagt — er braucht aber die Selbstbeobachtung aus SP-3 als Datenquelle.

### WP5: Sichtbarkeit und Aufsicht

**Zweck:** Verhindern, dass die Aussperrung selbst zum blinden Fleck wird.
**Schätzung:** S (1 Tag).

1. **WP5.1:** Ausgesperrte Container in der Container-/Serveransicht als „nicht überwacht" kennzeichnen — unterscheidbar von „unauffällig".
2. **WP5.2:** Je Container das Alter des letzten erfolgreichen Scans anzeigen.
3. **WP5.3:** **Aufsichtsregel:** älter als 3 × Zyklusintervall ⇒ Meldung, unabhängig davon, welcher Mechanismus die Ursache ist. Diese Regel ist wichtiger als die Aussperrung selbst, weil sie auch greift, wenn WP4 versagt.

**Ergebnis:** Ein nicht überwachter Container ist niemals als gesund lesbar.

**Abnahme:** Meldeweg absichtlich abklemmen; die Aufsichtsregel schlägt trotzdem an.

> 🟢 **WP5.3 erledigt** (2026-08-26) — der wichtigere Teil von WP5. `Services/Observability/ScanSupervisor.cs`: ein eigener Hintergrunddienst, der jede Minute prüft, ob ein Loop für einen Server seit mehr als **drei seiner eigenen Intervalle** keinen Zyklus mehr abgeschlossen hat, und dann `monitoring_stalled` meldet — **unabhängig von der Ursache**. Ein verkeilter Socket, ein ausgesperrter Container, ein pausierter Loop, eine unbehandelte Ausnahme und ein toter Thread sehen von hier aus gleich aus und bedeuten dasselbe.
>
> Damit die Aufsicht überhaupt urteilen kann, meldet **jeder Loop seine eigene Taktrate** mit (`RecordCycle(..., interval:)`). Ohne deklarierte Taktrate schweigt die Aufsicht — „seit zehn Minuten kein Zyklus" heißt bei einem Minutentakt etwas anderes als bei einem Sechs-Stunden-Takt, und eine geratene Schwelle hätte sie zur Lärmquelle gemacht. Eine Untergrenze von 5 Minuten verhindert, dass ein schneller Loop nach Sekunden jemanden weckt. Übersprungene Server (Kubernetes) gelten nie als stillstehend, sonst ginge der echte Fall im Rauschen unter.
>
> ⚠️ **Beinahe-Fehler, festgehalten:** Mein erster Testentwurf bestand aus **fünf Zusicherungen, die alle nur Schweigen prüften** — kein einziger verlangte, dass die Aufsicht anschlägt. Gegen einen Wächter, der nie meldet, wären alle fünf grün gewesen. Erst der Gegenbeweis machte es sichtbar: Aufsicht blind geschaltet ⇒ **nur der eine** Test rot, der das Anschlagen fordert, die anderen fünf blieben grün. Das ist heute Nacht der dritte Test, der nicht gemessen hat, was er behauptete.
>
> **Offen aus WP5:** WP5.1/WP5.2 (Darstellung ausgesperrter Container in der Oberfläche samt Alter des letzten Erfolgs) — reine UI-Arbeit. Die Zahlen dafür liegen jetzt in `ISelfMetrics`. Und: die Aufsicht muss beim Not-Aus (SP-5) ausdrücklich **nicht** pausierbar sein; das ist in Plan-0005 WP0 als eigenes Arbeitspaket vermerkt.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_log_scan_status`, `resume_log_scan` — Aussperrungen und Alter des letzten Erfolgs lesen; Aussperrung aufheben. Stufe: read / write, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Aufhebung per MCP wirkt im nächsten Zyklus und steht im Audit. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
Plan-0001 (WP1+WP2) ──> WP1 ──> WP2 ──> WP3 ──> WP4
                                          └────> WP5
```

- **Extern blockiert von:** Plan-0001 WP1/WP2 (harte Voraussetzung).
- **Liefert an:** SP-3 (Zähler), SP-4 (Signal), attackResponse AR-1 (Meldungen als Incident, sobald vorhanden).

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Angefragte Fensterbreite | `since`-Parameter im Proxy-Zugriffslog | ≤ `MaxLookback` |
| Zahl ausgesperrter Container | Selbstmetriken | jeder Eintrag hat eine offene Meldung |
| Alter des letzten Erfolgs je Container | Ansicht WP5.2 | < 3 × Intervall |
| Abrufdauer über Zyklen | Antwortzeit im Proxy-Log | konstant, nicht steigend |
| Zeit Störung → Meldung | manuelle Messung im Reproduktionsfall | < 4 min |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Stille Aussperrung | Container fällt lautlos aus der Überwachung | WP5.3 — die Aufsichtsregel ist der Ersatzmechanismus |
| Deckel verdeckt die Ursache | Abrufe schnell, Log wächst weiter | SP-7 misst die Loggröße; beide Pakete gemeinsam betrachten |
| Fehlerarten vermischt | entfernte Container erzeugen Alarme | WP2.1 trennt sie; Abnahmetest deckt genau das ab |
| Backoff kehrt nie zurück | dauerhaft blinder Fleck | Deckel 60 min + Test mit simulierter Erholung |
| Datenverlust wird verschwiegen | Lücke wirkt wie Ruhe | WP1.3 + WP4.1 benennen den Zeitraum |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 + WP2 | Fensterbreite im Proxy-Log konstant über 20 Zyklen |
| M2 | WP3 + WP4 | Reproduktion: eine Meldung nach drei Zyklen, < 4 min |
| M3 | WP5 | Meldeweg abgeklemmt, Aufsichtsregel schlägt an |
| M4 | Feldnachweis | 72 h auf BurgCloud ohne Fehlalarm, mit protokollierten Zählerwerten |

## Rückweg

`MaxLookback` und die Aussperrschwelle sind konfigurierbar. Erweist sich die Aussperrung im Feld als zu aggressiv, wird die Schwelle erhöht — der Fensterdeckel bleibt in jedem Fall bestehen, er ist die eigentliche Behebung.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [ ] Fensterbreite über 20 Zyklen nachweislich gedeckelt (Proxy-Log als Beleg)
- [ ] Reproduktion: Meldung in unter vier Minuten, gemessen
- [ ] 20 Zyklen kaputter Container = genau eine Meldung
- [ ] Entfernter Container erzeugt keine Aussperrungsmeldung
- [ ] Aufsichtsregel WP5.3 greift bei abgeklemmtem Meldeweg
- [ ] Ausgesperrte Container sind in der Oberfläche nicht als gesund lesbar
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
