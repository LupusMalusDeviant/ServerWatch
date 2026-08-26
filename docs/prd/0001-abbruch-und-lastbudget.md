# PRD-0001: Abbruch & Lastbudget (SP-1)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte (Hauptbetroffener), Nutzer von Whiskers als Produkt
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md)
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-1
- **Ersetzt:** —

## Problem / Motivation

Whiskers kann die Server, die es überwacht, überlasten, ohne es zu merken und ohne sich selbst zu bremsen.

Am 20.08.2026 lief BurgCloud (2 Kerne) innerhalb von zwei Minuten von 12 % auf 98 % CPU und blieb dort sechs Tage. Ursache war der Log-Monitor: `Task.WhenAny` bricht das *Warten* auf einen Docker-Aufruf ab, nicht den *Aufruf*. Die HTTP-Anfrage an dockerd lief weiter, bis der vorgelagerte Proxy sie nach 600 s kappte. Bei 60 s Zyklusdauer standen damit dauerhaft rund 10 Anfragen je betroffenem Container gleichzeitig offen; dockerd setzte 1,15 Mio. `read()`-Syscalls pro Sekunde ab und verbrauchte 184 % von 200 % CPU.

Der Defekt ist nicht auf den Log-Monitor beschränkt. `IDockerService` führt an keiner Stelle einen `CancellationToken`; Docker.DotNet bietet die Überladungen an, die Kette gibt sie nicht durch. Und **jeder** der Hintergrund-Loops (LogMonitor, Metrics, CVE, Health, ImageUpdate) hat sein eigenes Timeout und keine Kenntnis davon, was die anderen gerade auf demselben Server tun. Es gibt keine Stelle, an der die Gesamtlast, die Whiskers auf einem Server erzeugt, begrenzt oder auch nur gezählt wird.

## Ziele

- Ein abgelaufener Aufruf endet auch auf dem Server, nicht nur in Whiskers.
- Die Last, die Whiskers auf einem einzelnen Server erzeugt, ist nach oben begrenzt — unabhängig davon, welcher Loop sich falsch verhält.
- Ein Server, der nicht mehr antwortet, wird entlastet statt weiter befragt.
- Die Begrenzung ist im Betrieb sichtbar und im Testlauf beweisbar.

## Non-Goals

- **Keine** Änderung an der Alarm- oder Bewertungslogik — das ist SP-4.
- **Keine** neuen Metriken-Endpunkte oder Oberflächen — das ist SP-3 (dieses Paket liefert nur die Zähler, die SP-3 exportiert).
- **Keine** Veränderung am überwachten Server (Log-Rotation, Neustarts) — das ist SP-7 bzw. bleibt Freigabe-pflichtig.
- **Keine** Priorisierung nach Geschäftswert. Fairness zwischen Loops ja, Business-Priorisierung nein.
- **Kein** Umbau der Loops selbst auf ein gemeinsames Scheduling-Modell.

## Zielgruppen / Personas

### Flottenbetreiber (Hauptnutzer)

- Kontext: betreibt kleine Server (1–4 Kerne), auf denen Nutzlast und Whiskers-Zugriff sich denselben Kern teilen.
- Pain Point: Ein Monitoring-Werkzeug, das die Maschine belastet, die es überwachen soll, ist schlimmer als keines — es erzeugt Fehler, die es dann meldet.

### Whiskers-Entwickler

- Kontext: baut weitere Loops (Events-Stream F6, K8s-Loops GAP-1).
- Pain Point: Es gibt keine Stelle, an der ein neuer Loop automatisch mit-begrenzt wird. Jeder neue Loop ist eine neue Gelegenheit für denselben Fehler.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | `IDockerService` führt auf allen netzwerkgebundenen Operationen einen `CancellationToken`, der bis in die Docker.DotNet-Aufrufe durchgereicht wird. | Must |
| FR-02 | Der Log-Monitor verwendet eine `CancellationTokenSource(LogFetchTimeout)` statt `Task.WhenAny`; ein Timeout bricht die Anfrage tatsächlich ab. | Must |
| FR-03 | Ein `IServerBudget` je Server begrenzt die gleichzeitigen Docker-/API-Aufrufe (Default: 4) und wird von **allen** Hintergrund-Loops benutzt. | Must |
| FR-04 | Interaktive Aufrufe (UI, MCP, Agent) laufen in einem getrennten Kontingent und können von Hintergrund-Loops nicht ausgehungert werden. | Must |
| FR-05 | Je (Server, Container, Operationsart) ist höchstens **eine** Anfrage gleichzeitig offen; ein zweiter Versuch wird verworfen, nicht eingereiht. | Must |
| FR-06 | Ein Circuit Breaker je Server öffnet nach n aufeinanderfolgenden Timeouts/Fehlern (Default 5), lässt in halboffenem Zustand einen Probe-Aufruf zu und schließt nach Erfolg. | Must |
| FR-07 | Öffnen und Schließen des Circuits erzeugt eine Benachrichtigung — Selbstdrosselung ist niemals still. | Must |
| FR-08 | Das Budget zählt: laufende Aufrufe, Wartezeit auf einen Platz, Timeouts, verworfene Doppelanfragen, Circuit-Zustand. | Must |
| FR-09 | Budgetgrößen und Schwellen sind je Server konfigurierbar, mit belastbaren Defaults für einen 2-Kern-Server. | Should |
| FR-10 | Ein Testlauf beweist die Invariante aus FR-05 gegen einen absichtlich langsamen Backend-Doppelgänger. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_server_budget` (read): Budgetauslastung, laufende Aufrufe, Wartezeit und Circuit-Zustand je Server. Ohne dieses Werkzeug kann der Agent bei einer Lastfrage Whiskers selbst nicht als Verursacher prüfen. | Must |

## Nicht-Funktionale Anforderungen

- **Rückwärtskompatibel:** Bestehende Aufrufer ohne Token bleiben übersetzbar (Default-Parameter `default`), damit der Umbau in Etappen mergebar ist.
- **Kein Durchsatzverlust im Normalfall:** Bei gesunden Servern (Antwortzeiten < 200 ms) darf ein voller Scan-Zyklus nicht länger dauern als heute, gemessen über 10 Zyklen.
- **Fail-open bei Konfigurationsfehlern:** Ein kaputt konfiguriertes Budget darf Whiskers nicht blind machen — es fällt auf den Default zurück und meldet das.
- **Keine neuen Abhängigkeiten:** Semaphore und Circuit Breaker werden mit Bordmitteln gebaut (`SemaphoreSlim`), kein Polly.

## User Stories

- **US-01:** Als Flottenbetreiber möchte ich, dass Whiskers auf einem überlasteten Server *weniger* fragt statt mehr, damit das Monitoring die Störung nicht verstärkt.
- **US-02:** Als Flottenbetreiber möchte ich sehen, dass Whiskers sich gerade selbst drosselt, damit ich die Ursache suchen kann statt der Wirkung.
- **US-03:** Als Entwickler möchte ich, dass ein neuer Loop automatisch unter dem Budget läuft, damit ich denselben Fehler nicht erneut einbauen kann.

### Flow für US-01

```
Given ein Server, dessen Docker-API dauerhaft > 15 s braucht
When der Log-Monitor seinen Zyklus startet
Then läuft höchstens 1 Anfrage je Container, jede endet nach 15 s serverseitig,
     nach 5 Fehlschlägen öffnet der Circuit, weitere Aufrufe entfallen sofort,
     und eine Benachrichtigung nennt Server, Grund und Dauer
```

## Akzeptanzkriterien

- FR-01 bis FR-08 und FR-10 umgesetzt.
- Der Reproduktionsfall aus dem Vorfallsbericht läuft ohne Lastanstieg: ein Container mit einem Log jenseits der Timeout-Grenze wird über 10 Zyklen gescannt, die Zahl gleichzeitig offener Log-Anfragen bleibt ≤ 1, die Abrufdauer wächst über die Zyklen nicht.
- Auf einem realen Server mit absichtlich großem Log bleibt `dockerd` unter 20 % CPU über 30 Minuten Scan-Betrieb.
- Öffnender Circuit erzeugt genau eine Benachrichtigung, kein Sturm.
- MCP: `get_server_budget` liefert am **laufenden** MCP-Server dieselben Werte wie die Oberfläche; ein Agentenlauf zu „warum ist Server X langsam?“ ruft es ohne weitere Anweisung ab.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Gleichzeitig offene Log-Leser | `for f in /proc/$(pidof dockerd)/fd/*; do readlink $f; done \| grep -c 'json.log'` | 0–1 je Container | ≥ 2 dauerhaft |
| Leerlauf-Spin von dockerd | `awk '/^syscr\|^rchar/{print $1,$2}' /proc/$(pidof dockerd)/io` | `syscr` steigt proportional zu `rchar` | `syscr` explodiert, `rchar` kaum — Arbeit ohne Ausbeute |
| dockerd-CPU während eines Scan-Zyklus | `top`/`pidstat` auf dem Zielserver | < 20 % auf 2 Kernen | > 50 % anhaltend |
| Budget-Wartezeit | `self:` Zähler aus FR-08 (von SP-3 exportiert) | Median < 100 ms | Median > 2 s ⇒ Budget zu klein **oder** ein Loop zu gierig |
| Verworfene Doppelanfragen | Zähler FR-08 | nahe 0 im Normalbetrieb | dauerhaft > 0 ⇒ ein Loop feuert schneller, als der Server antwortet |
| Circuit-Zustand je Server | Zähler FR-08 + Benachrichtigung | geschlossen | offen ohne dazugehörige Meldung ⇒ FR-07 kaputt |

## Woran ich sehe, dass es bricht

Der Fix hat eigene Versagensarten, und sie sind **leiser** als das Problem, das er behebt. Genau darauf ist zu achten:

1. **Stille Blindheit statt lauter Last.** Ein Circuit Breaker, der zu früh oder dauerhaft öffnet, macht Whiskers auf diesem Server blind — und zwar ohne Symptome auf dem Server. **Messstelle:** Anteil der Zyklen je Server, in denen der Circuit offen war. Steigt er über 5 % und es gibt keine Serverstörung, ist die Schwelle falsch. **Gegenprobe:** Der letzte erfolgreiche Scan je Server wird angezeigt; ist er älter als drei Zyklen, muss das gemeldet werden — auch wenn alles „ruhig" aussieht.
2. **Aushungern statt Überlasten.** Wenn der CVE-Scan das Budget dauerhaft belegt, laufen Health-Checks nicht mehr. Symptom von außen: alles grün, weil nichts geprüft wird. **Messstelle:** Zyklusdauer je Loop je Server. Wächst die Dauer eines Loops über das Doppelte seines Intervalls, verhungert er.
3. **Der Token wird durchgereicht, aber nicht benutzt.** Eine Signatur mit `CancellationToken`, die intern die tokenlose Überladung aufruft, sieht in jedem Review korrekt aus und ändert nichts. **Gegenprobe:** genau der Test aus FR-10 muss **vor** der Änderung fehlschlagen. Ein Test, der auch vorher grün ist, beweist nichts.
4. **Das Budget zählt Aufrufe, aber nicht Arbeit.** 4 gleichzeitige Aufrufe gegen ein 500-MB-Log sind schlimmer als 40 gegen kleine Logs. **Messstelle:** `syscr`-Rate von dockerd gegen die Zahl der gelieferten Zeilen. Ein hohes Verhältnis heißt: die Begrenzung greift an der falschen Größe (Abhilfe: SP-2 deckelt das Fenster).
5. **Der Regressionspfad.** Ein neuer Loop, der `IServerBudget` nicht benutzt, umgeht alles. **Gegenprobe:** ein Architektur-Test, der fehlschlägt, sobald ein `BackgroundService` Docker-Operationen ohne Budget aufruft.

## Do's

- **Erst FR-01/FR-02, dann FR-03.** Ohne echten Abbruch ist das Budget ein Zähler ohne Wirkung: die alten Anfragen laufen weiter und belegen den Server, nicht das Budget.
- **Den Test aus FR-10 zuerst schreiben** und rot sehen, bevor die Behebung beginnt.
- **Defaults für die kleinste Zielmaschine wählen** (2 Kerne), nicht für die Entwicklungsmaschine.
- **Jede Drosselung melden**, mit Server, Grund und Dauer.
- **Interaktive Pfade getrennt halten** — ein blockierender Hintergrund-Loop darf die Oberfläche nie einfrieren.

## Don'ts

- **Nicht** `Task.WhenAny` durch `Task.WaitAsync` ersetzen und für erledigt halten — auch das lässt die Anfrage weiterlaufen.
- **Nicht** das Timeout hochsetzen, um Fehlschläge zu vermeiden. Das vergrößert den Schaden pro Fehlschlag.
- **Nicht** den Circuit ohne halboffenen Zustand bauen — sonst braucht ein wieder gesunder Server einen Neustart von Whiskers.
- **Keine** Wiederholung mit Sofort-Retry im Fehlerfall. Retry ohne Backoff ist genau der Mechanismus, der den Vorfall stabil gehalten hat.
- **Nicht** je Loop ein eigenes Budget bauen. Der Server sieht die Summe, also muss die Summe begrenzt werden.

## Abhängigkeiten

- **Blockiert:** SP-2, SP-3, SP-5, und mittelbar GAP-1.
- **Wird blockiert von:** nichts. Startfähig.

## Offene Fragen

- **F-01:** Gilt das Budget auch für den Agenten, wenn er im Auftrag eines Menschen handelt? Vorschlag: ja, aber im interaktiven Kontingent (FR-04).
- **F-02:** Default-Budgetgröße — 4 gleichzeitige Aufrufe je Server ist eine Schätzung. Vor dem Festschreiben gegen Badwolf und BurgCloud messen.
