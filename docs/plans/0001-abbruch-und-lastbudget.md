# Plan-0001: Abbruch & Lastbudget (SP-1)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0001](../prd/0001-abbruch-und-lastbudget.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Der Vorfall vom 26.08. hat drei Ursachen, von denen dieser Plan die erste und wichtigste behebt: Ein abgelaufener Docker-Aufruf endet in Whiskers, aber nicht auf dem Server. Solange das so ist, sind alle weiteren Schutzmaßnahmen wirkungslos — deshalb steht dieser Plan vor allen anderen.

Der Plan hat zwei Hälften, die in dieser Reihenfolge laufen müssen: **echter Abbruch** (WP1–WP2), dann **Begrenzung** (WP3–WP5). Umgekehrt ergibt die Begrenzung ein Bild ohne Wirkung.

## Ziele

- Abgelaufene Aufrufe enden serverseitig.
- Die Gesamtlast je Server ist begrenzt, unabhängig vom Loop.
- Ein neuer Loop kann die Begrenzung nicht versehentlich umgehen.

## Arbeitspakete

### WP1: `CancellationToken` durch die Docker-Kette

**Zweck:** Die technische Voraussetzung für alles Weitere.
**Schätzung:** M (2–3 Tage) — viele Berührungspunkte, wenig Logik.

1. **WP1.1:** `IDockerService` um `CancellationToken ct = default` auf allen netzwerkgebundenen Methoden erweitern. Default-Parameter, damit der Umbau in Etappen mergebar bleibt.
2. **WP1.2:** `DockerService` und `Services/Docker/Operations/*.cs` reichen den Token an die Docker.DotNet-Überladungen durch. **Prüfen, dass tatsächlich die Token-Überladung aufgerufen wird** — nicht nur die Signatur ergänzt.
3. **WP1.3:** `IWorkloadProvider` führt bereits Token; die Docker-Implementierung durchreichen statt verwerfen.
4. **WP1.4:** Aufrufer schrittweise auf die Token-Variante ziehen: Hintergrund-Loops zuerst (dort entsteht der Schaden), UI/MCP danach.

**Ergebnis:** Ein abgebrochener Aufruf schließt die HTTP-Verbindung zu dockerd.

**Abnahme:** Integrationstest gegen einen absichtlich langsamen HTTP-Doppelgänger: Nach `cts.Cancel()` ist die Verbindung serverseitig geschlossen — geprüft am Doppelgänger, nicht am Client.

### WP2: Log-Monitor auf echte Abbrüche umstellen

**Zweck:** Die konkrete Fehlerstelle aus dem Vorfall.
**Schätzung:** S (0,5 Tage).

1. **WP2.1:** In `LogMonitorService.FetchLogsAsync` (Z. 256 ff.) `Task.WhenAny` durch `CancellationTokenSource(LogFetchTimeout)` ersetzen, verkettet mit dem Zyklus-Token.
2. **WP2.2:** Die Protokollmeldung korrigieren: „skipped this cycle" war sachlich falsch — übersprungen wurde die Auswertung, nicht die Arbeit. Neue Formulierung nennt den tatsächlichen Abbruch.
3. **WP2.3:** Denselben Umbau in allen weiteren `Task.WhenAny`-Timeout-Stellen. **Vorher suchen:** `grep -rn "Task.WhenAny" --include=*.cs src/`.

**Ergebnis:** Der im Vorfall beschriebene Aufbau von 13 gleichzeitigen Scans ist strukturell unmöglich.

> 🟢 **WP1 + WP2 erledigt** (2026-08-26). Die beiden Invarianten aus M1 sind **grün, ohne dass die Tests angefasst wurden** — rot vorher, grün nachher, das ist der Beweis. **636/636**, Build 0 Fehler.
>
> **Die entscheidende Zeile** war `ContainerOperations.cs:289`: die Leseschleife übergab `CancellationToken.None` an `ReadOutputAsync`. Ein Token in der Signatur allein hätte nichts geändert — genau die Versagensart, vor der PRD-0001 warnt („Der Token wird durchgereicht, aber nicht benutzt"). Jetzt reicht die Kette `IDockerService` → `DockerService` → `ContainerOperations` bis in `client.Containers.GetContainerLogsAsync(..., ct)` **und** in die Leseschleife.
>
> **WP2:** `FetchLogsAsync` nutzt eine verkettete `CancellationTokenSource` mit `CancelAfter` statt `Task.WhenAny`. Das `when`-Filter unterscheidet sauber: nur das *eigene* Timeout wird zur `TimeoutException`, ein abgebrochener Shutdown-Token propagiert weiter — sonst sähe ein Herunterfahren wie eine Flotte kaputter Container aus.
>
> **Zwei weitere Fundstellen derselben Falle** (WP2.3-Suche, im Vorfallsbericht nicht erwähnt): `ContainerOperations.ListAllContainersDetailedAsync:120` und `SystemInfoOperations.GetAllServerSystemInfoAsync:151` — beide bounden je Server mit 8 s über `Task.WhenAny`, ließen die Anfrage also ebenfalls weiterlaufen. Beide laufen **in jedem Zyklus für jeden Server**, nicht nur für auffällige Container. Beide auf `CancellationTokenSource(perServerTimeout)` umgestellt; `ListContainersAsync` und `GetServerSystemInfoAsync` führen dafür jetzt ebenfalls einen Token.
>
> **Bewusster Zuschnitt:** Der Token liegt auf den drei netzwerkgebundenen Methoden, die tatsächlich ein Timeout haben. Die übrigen `IDockerService`-Methoden kennen heute gar keine Frist — dort einen Token zu ergänzen, den niemand auslöst, wäre Signatur-Kosmetik ohne Verhaltensgewinn. Sie kommen mit WP3/WP4 dran, sobald das Budget eine Frist setzt.

**Abnahme:** Der Prüfbefehl aus dem Vorfallsbericht auf einem Testserver — offene `json.log`-Deskriptoren von dockerd bleiben ≤ 1 je Container über 10 Zyklen.

### WP3: `IServerBudget`

**Zweck:** Die Gesamtlast je Server begrenzen, unabhängig vom Verursacher.
**Schätzung:** M (2 Tage).

1. **WP3.1:** `Services/Docker/Budget/IServerBudget` + Implementierung: `SemaphoreSlim` je Server, getrennte Kontingente für Hintergrund (Default 4) und interaktiv (Default 4).
2. **WP3.2:** Single-Flight je `(serverId, containerId, operation)`: eine laufende Anfrage; ein zweiter Versuch wird **verworfen** und gezählt, nicht eingereiht.
3. **WP3.3:** Einhängen an der engsten gemeinsamen Stelle — in `DockerConnectionManager`/`Operations`, nicht in jedem Loop. Ziel: Ein Loop kann das Budget nicht umgehen, weil er es nicht kennt.
4. **WP3.4:** Konfiguration je Server in `ServerConfig`, mit Defaults für 2-Kern-Maschinen.

**Ergebnis:** Jeder ausgehende Aufruf läuft durch das Budget.

**Abnahme:** Lasttest mit 5 gleichzeitig laufenden Loops gegen einen Doppelgänger: nie mehr als 4 gleichzeitige Hintergrundanfragen; interaktive Aufrufe kommen währenddessen in < 1 s durch.

> 🟢 **WP3 erledigt** (2026-08-26), bis auf WP3.2 (siehe unten). `Services/Docker/Budget/` mit `IServerBudget` + `ServerBudget`: zwei `SemaphoreSlim`-Bahnen je Server, angelegt beim ersten Kontakt. **Eingehängt in `DockerConnectionManager.ExecuteAsync`** — der eine Punkt, durch den jeder Docker-Aufruf läuft. Damit ist ein künftiger Loop an dem Tag begrenzt, an dem er geschrieben wird, ohne dass sein Autor vom Budget wissen muss; genau die Eigenschaft, die am 26.08. fehlte, als fünf Loops jeweils nur sich selbst überwachten. Der Wiederholungsversuch nach einem Verbindungsfehler nimmt einen eigenen Platz — es ist eine zweite Anfrage, die der Server bedienen muss.
>
> **Hintergrund vs. interaktiv** über einen `AsyncLocal`-Bereich (`BackgroundScope()`), gesetzt im Scan-Zyklus des Log-Monitors. Unmarkiert = interaktiv, und das ist die sichere Richtung: einen Loop für einen Nutzer zu halten kostet einen Slot, einen Nutzer für einen Loop zu halten kostet Antwortzeit.
>
> **Defaults 4/4 je Server**, konfigurierbar über `ServerBudget:` samt `PerServer`-Überschreibungen; Werte < 1 werden auf 1 angehoben statt abgelehnt — eine vertippte 0 darf keinen Host stillegen, sie degradiert zu „einer nach dem anderen".
>
> **Gegenbeweis geführt:** Semaphore ausgehebelt ⇒ **alle 6** Budget-Tests rot, danach zurückgebaut. Zusätzlich ist ein Differenztest fest eingebaut (`A_raised_limit_really_raises_the_ceiling`): ohne ihn könnte die Obergrenzen-Prüfung eine Zahl behaupten, die gar nicht durchgesetzt wird.
>
> ⚠️ **Korrektur zu diesem Block (nachgetragen bei WP6.3):** Der Satz oben, das Budget sitze am Punkt, „durch den jeder Docker-Aufruf läuft", **war falsch**. Beim Bau des Architekturtests kam heraus: von 24 Aufrufstellen in `Services/Docker/Operations/` gingen nur **3** über `ExecuteAsync`; 21 holten sich direkt über `GetClient` einen nackten Client — **darunter der Log-Abruf aus dem Vorfall selbst**. Das Budget sah vollständig aus und deckte fast nichts ab. Behebung und verbleibende Liste siehe WP6.3.
>
> 🟢 **WP3.2 erledigt** — Single-Flight liegt in `ServerBudget.RunAsync` (Parameter `singleFlightKey`, greift nur im Hintergrund-Lauf: für einen Loop ist ein verworfener Aufruf ein gesparter, für einen Menschen wäre er eine hängende Oberfläche).
>
> 🟢 **Budget-Abdeckung abgeschlossen (2026-08-27) — und zwar nicht durch das Umstellen aller Stellen.**
> Von 24 Aufrufstellen laufen jetzt **8 unter dem Budget statt 4**; die verbliebenen 16 sind mit Begründung
> ausgenommen. Umgestellt wurde alles, was **stetiger Hintergrund-Lesezugriff** ist:
>
> | Aufruf | Warum er zählt |
> |---|---|
> | Container-Statistik | je Container alle 30 s — die mit Abstand größte Einzelquelle an Docker-Verkehr |
> | Host-System-Info | je Server je Metrikzyklus |
> | Container-Zustand | zwei Hintergrundschleifen (Health, Auto-Update) |
> | Image-Digest | je Image je Update-Durchlauf |
> | Log-Abruf | der Aufruf, um den es im Vorfall ging (bereits vorher) |
>
> **Der Rest bleibt draußen, und der ursprünglich notierte Grund war falsch.** Dort stand, mutierende
> Operationen seien ausgenommen, weil sie „nie automatisch wiederholt werden dürfen" — `ExecuteGuardedAsync`
> wiederholt gar nicht, das war nie das Risiko. Der echte Grund ist der **Circuit Breaker**: Er weist Aufrufe
> an einen Server ab, den er aufgegeben hat, und Start/Stop/Neustart/Entfernen sind genau das, wonach ein
> Mensch greift, wenn ein Server in Schwierigkeiten steckt — also in dem Moment, in dem der Circuit am
> ehesten offen ist. Die Reparatur ausgerechnet dann wegzunehmen wiegt schwerer als die eingesparte Last.
>
> Dazu die langlaufenden Operationen (Image-Pull, Container-Neuerzeugung): Ein Budget-Platz, der minutenlang
> gehalten wird, hungert die Gesundheitsprüfung und den Log-Scan desselben Servers aus. Der Deckel ist gegen
> viele kleine Aufrufe gedacht, nicht gegen diese.
>
> Der Ratchet-Test trägt jede dieser Begründungen jetzt im Klartext. Gegenprobe: eine neue Umgehung eingebaut
> → Test rot mit klarer Meldung.
>
> **Offen:** der Feldnachweis über 48 h auf zwei realen Servern — braucht einen Deploy.
>
> ⚠️ **Beobachtung, nicht wegerklärt:** In einem von fünf vollen Läufen fiel `BackupServiceTests.Validate_accepts_an_equal_or_older_schema` einmalig aus. In Isolation und in vier Folgeläufen grün; kein Bezug zu dieser Änderung erkennbar (kein Docker-Pfad). Als möglicher Flake festgehalten, nicht als behoben.
>
> Ergebnis: Build 0 Fehler, **642/642 Tests grün** (vorher 636). `Services/Docker/Budget/README.md` neu, `Services/Docker/README.md` ergänzt.

### WP4: Circuit Breaker und Meldung

**Zweck:** Einen nicht antwortenden Server entlasten statt weiter befragen.
**Schätzung:** S (1 Tag).

1. **WP4.1:** Zustandsautomat je Server: geschlossen → offen (nach 5 Fehlschlägen in Folge) → halboffen (nach 60 s, ein Probe-Aufruf) → geschlossen.
2. **WP4.2:** Im offenen Zustand schlagen Aufrufe sofort fehl, ohne Netzverkehr.
3. **WP4.3:** Öffnen und Schließen erzeugen je eine Benachrichtigung über den vorhandenen Weg — **Pflicht**, keine stille Drosselung.
4. **WP4.4:** Zustand in der Serveransicht darstellen.

**Ergebnis:** Selbstdrosselung mit Rückweg und Sichtbarkeit.

**Abnahme:** Server abschalten → Circuit öffnet, genau eine Meldung. Server wieder anschalten → Circuit schließt binnen 60 s, Entwarnung.

> 🟢 **WP4 erledigt** (2026-08-26). `IServerCircuitBreaker` + `ServerCircuitBreaker` in derselben Budget-Schicht, eingehängt in `DockerConnectionManager.ExecuteAsync`: `ThrowIfOpen` vor dem Aufruf, `RecordSuccess`/`RecordFailure` danach. Zustandsautomat Closed → Open (Default 5 Fehlschläge in Folge) → HalfOpen nach 60 s mit **genau einem** Probe-Aufruf → Closed. Beides konfigurierbar über `ServerBudget:CircuitFailureThreshold` / `:CircuitCooldownSeconds`.
>
> **Was zählt als Fehlschlag:** nur Transportfehler und eigene Timeouts. Ein „No such container" sagt nichts über die Erreichbarkeit des Hosts — es mitzuzählen würde einen kerngesunden Server pausieren, weil jemand nach einem gelöschten Container gefragt hat. Und beim Wiederholungsversuch zählt nur der Fehlschlag **nach** frischem Tunnel: die erste Ausnahme ist genau die Störung, für die es den Retry gibt.
>
> **Jede Selbstdrosselung wird gemeldet** (`server_throttled` / `server_throttling_ended`, im `NotificationFormatter` mit eigenem Text und Link auf `/servers`). Der Meldungstext sagt ausdrücklich, dass Whiskers **sich selbst** drosselt und der Server währenddessen nicht geprüft wird — sonst ist ein offener Circuit von „alles ruhig" nicht zu unterscheiden. Genau eine Meldung je Übergang: ein dauerhaft toter Host erzeugt ein Ereignis, nicht eines je Cooldown.
>
> **Ein Test hat einen echten Logikfehler gefunden:** `A_failed_probe_reopens_the_circuit_without_a_second_announcement` fiel zunächst durch — ein fehlgeschlagener Probe-Aufruf galt als „neu geöffnet" und meldete ein zweites Mal. Bei einem dauerhaft toten Server hätte das alle 60 Sekunden eine weitere „Server gedrosselt"-Meldung erzeugt, also genau die Alarmflut, die Kanäle stummschalten lässt. Behoben: nur der Übergang **aus `Closed` heraus** ist eine Nachricht.
>
> **Gegenbeweis geführt:** Meldungen unterdrückt ⇒ 4 der 7 Tests rot, darunter alle drei, die die Sichtbarkeit sichern. Zurückgebaut.
>
> Ergebnis: Build 0 Fehler, **649/649 Tests grün** (vorher 642). `Budget/README.md` um den Abschnitt „The circuit is never silent" ergänzt.

### WP5: Zähler

**Zweck:** Die Rohdaten für SP-3.
**Schätzung:** S (0,5 Tage).

1. **WP5.1:** Zähler in `IServerBudget`: laufende Aufrufe, Wartezeit (Histogramm), Timeouts, verworfene Doppelanfragen, Circuit-Zustand — je Server und Operationsart.
2. **WP5.2:** Als Schnittstelle bereitstellen, ohne Exportformat festzulegen (das ist SP-3).

**Ergebnis:** SP-3 kann direkt darauf aufsetzen.

### WP6: Invarianten absichern

**Zweck:** Verhindern, dass der Fehler zurückkommt.
**Schätzung:** S (1 Tag).

1. **WP6.1:** Invariantentest „höchstens 1 offene Anfrage je (Server, Container, Operation)" gegen den langsamen Doppelgänger, über 10 Zyklen.
2. **WP6.2:** Invariantentest „Abrufdauer wächst über Zyklen nicht" — der zweite Teil des Prüfkriteriums aus dem Vorfallsbericht.
3. **WP6.3:** Architekturtest: schlägt fehl, sobald ein `BackgroundService` Docker-Operationen an `IServerBudget` vorbei aufruft (Reflexion über die Aufrufkette oder eine Registrierungsliste).
4. **WP6.4:** Beide Invariantentests in die reguläre CI, nicht als manueller Job.

**Ergebnis:** Ein Rückfall bricht den Build.

> 🟢 **WP3.2, WP5 und WP6.3 erledigt** (2026-08-26) — und WP6.3 hat den wichtigsten Befund des ganzen Pakets geliefert.
>
> **Der Befund:** Von 24 Docker-Aufrufstellen in `Services/Docker/Operations/` gingen nur **3** über `ExecuteAsync`. Die anderen 21 holten sich über `GetClient` einen nackten Client — **einschließlich `GetContainerLogsAsync`, also genau des Aufrufs, um den es am 26.08. ging**. Budget und Circuit Breaker waren gebaut, eingehängt, getestet — und deckten den Vorfallspfad nicht ab. Ohne den Architekturtest wäre das nicht aufgefallen; die Statusnotiz zu WP3 behauptete das Gegenteil und ist dort jetzt korrigiert.
>
> **Behebung:** Neues `ExecuteGuardedAsync` — Budget und Circuit **ohne** Wiederholungsversuch. Bewusst nicht alle 21 Stellen auf `ExecuteAsync` umgebogen: das hätte mutierenden Operationen (`create`, `start`, `remove`) eine automatische Wiederholung gegeben, die sie nie hatten. Einen Container-Start zu verdoppeln, um eine Lastbegrenzung zu gewinnen, ist ein schlechter Tausch. Der Log-Abruf läuft jetzt darüber — geführt statt wiederholend, weil ein Retry den Log-Strom von vorn beginnen und nicht fortsetzen würde.
>
> **WP6.3 als Ratsche statt Häkchen:** `DockerBudgetCoverageTests` fixiert die verbleibenden Umgehungen je Datei (ContainerOperations 7, Lifecycle 4, Network 5, Image 2, HostShell 1, SystemInfo 1). Eine neue bricht den Build; eine beseitigte **auch** — die Liste muss dann angepasst werden, wodurch jede Verbesserung im Diff sichtbar wird. Dazu ein eigener Test, der den Log-Abruf einzeln festnagelt: ginge er je zum nackten Client zurück, wäre das ganze Paket still entwertet.
>
> **WP3.2 mit geändertem Zuschnitt:** Single-Flight gilt **nur für Hintergrundarbeit**. Der Plan sah „zweiter Versuch wird verworfen" allgemein vor; für einen interaktiven Aufruf hieße das eine Fehlermeldung statt einer Antwort, während die Last ohnehin schon vom Budget gedeckelt ist. Für einen Loop kostet ein verworfener Versuch eine Runde — akzeptabel. Verworfene Duplikate werden gezählt.
>
> **WP5:** Die Zähler stehen in `ServerBudgetSnapshot` (laufende Aufrufe je Bahn, Limits, Starts, Wartezeit gesamt und Maximum, verworfene Duplikate) und `ServerCircuitSnapshot` (Zustand, Fehlschläge in Folge, Öffnungszeitpunkt, Grund). Als Schnittstelle bereitgestellt, ohne Exportformat — das ist SP-3.
>
> Ergebnis: Build 0 Fehler, **651/651 Tests grün**.

**Abnahme:** WP6.1 und WP6.2 **müssen auf dem Stand vor WP1 rot sein.** Ein Test, der auch vorher grün ist, beweist nichts und wird verworfen.

> 🔴 **M1 erreicht — die Wächter sind rot** (2026-08-26). `LogMonitorLoadInvariantTests` mit zwei Zusicherungen: höchstens eine Anfrage je Container gleichzeitig, und keine Anhäufung über die Zyklen. Gegen den heutigen Stand **beide rot**, mit dem Vorfall im Kleinen nachgebildet:
>
> ```
> local/c-proxy:  4 concurrent
> local/c-tunnel: 4 concurrent
> peak 7 concurrent log requests across 2 containers over 10 cycles
> ```
>
> Der Bericht maß 7 (socket-proxy) und 6 (ghostunnel) offene Dateideskriptoren — dieselbe Anhäufung, nur mit einem Verhältnis von 8 statt 10 zwischen Fetch-Dauer und Timeout.
>
> **Aufbau:** `FakeDocker` um `FetchDelay` und Nebenläufigkeitsmessung erweitert (Spitzenwert je Container und flottenweit); Default 0, alle bestehenden Tests laufen unverändert. Damit zehn Zyklen in Sekunden statt in zweieinhalb Minuten durchlaufen, ist das Fetch-Timeout jetzt über einen optionalen Konstruktorparameter setzbar (`_logFetchTimeout`, Default weiterhin 15 s — **keine** Verhaltensänderung in Produktion, und die Änderung repariert nichts).
>
> **Bewusste Abweichung bei WP6.2:** Der Plan formuliert die zweite Invariante als „Abrufdauer wächst über die Zyklen nicht". Diese Facette ist mit SP-1 allein **nicht** erreichbar: die Dauer wächst, weil das `since`-Fenster bei jedem Fehlschlag größer wird, und das Wasserzeichen behebt erst SP-2. Hier steht deshalb die Facette, die SP-1 tatsächlich behebt — die Anhäufung abgebrochener Anfragen, also genau die Zahl, die im Bericht als offene Deskriptoren gemessen wurde. Die Fenster-Ratsche gehört als Zusicherung in Plan-0002 und ist dort in der Abnahme schon benannt.
>
> ⚠️ **Die Testsuite ist ab hier absichtlich rot** (632/634), bis WP1+WP2 stehen. Das ist der Zweck von M1, kein kaputter Stand.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_server_budget` — Budget, laufende Aufrufe, Wartezeit, Circuit-Zustand je Server. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Ein Agentenlauf zur Lastfrage ruft es selbstständig ab und nennt Whiskers als möglichen Verursacher. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2 ──> WP6.1/WP6.2
 └────> WP3 ──> WP4 ──> WP5
WP6.3 kann parallel ab WP3 laufen
```

- **WP1 vor allem anderen.** Ohne echten Abbruch ist WP3 ein Zähler ohne Wirkung.
- **WP6.1/WP6.2 vor WP1 schreiben** (rot sehen), erst danach beheben.
- Blockiert extern: SP-2, SP-3, SP-5, GAP-1, GAP-2, GAP-3.

## Prüf- und Messstellen im Betrieb

| Messstelle | Befehl / Quelle | Erwartung |
|---|---|---|
| Offene Log-Leser | `for f in /proc/$(pidof dockerd)/fd/*; do readlink $f; done \| grep -c 'json.log'` | ≤ 1 je Container |
| Leerlauf-Spin | `awk '/^syscr\|^rchar/{print $1,$2}' /proc/$(pidof dockerd)/io` | `syscr` proportional zu `rchar` |
| dockerd-CPU | `pidstat -p $(pidof dockerd) 5` während eines Scanzyklus | < 20 % auf 2 Kernen |
| Budget-Wartezeit | Zähler aus WP5 | Median < 100 ms |
| Circuit-Öffnungen | Zähler + Meldungen | jede Öffnung hat genau eine Meldung |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Budget zu klein gewählt | Loops verhungern, alles wirkt gesund, weil nichts geprüft wird | Zyklusdauer je Loop überwachen; Alarm, wenn sie das doppelte Intervall überschreitet |
| Circuit öffnet zu leicht | stille Blindheit auf einem gesunden Server | Anteil Zyklen mit offenem Circuit messen; über 5 % ohne Serverstörung ⇒ Schwelle korrigieren |
| Token ergänzt, aber nicht benutzt | sieht behoben aus, ist es nicht | WP6.1 muss vorher rot sein — das ist der einzige belastbare Nachweis |
| Umbau bricht bestehende Aufrufer | Regressionen quer durch die App | Default-Parameter, etappenweiser Merge, DI-Boot-Gate in beiden Auth-Modi je Etappe |
| Interaktive Pfade blockieren | Oberfläche friert bei Serverstörung ein | getrennte Kontingente (WP3.1), Abnahmetest misst interaktive Antwortzeit unter Last |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP6.1/WP6.2 geschrieben, **rot** | Testlauf-Protokoll mit Fehlschlag |
| M2 | WP1 + WP2 | dieselben Tests grün; Deskriptor-Zählung auf einem Testserver |
| M3 | WP3 + WP4 | Lasttest; Circuit öffnet und schließt mit Meldung |
| M4 | WP5 + WP6.3 + CI | Architekturtest bricht bei einem absichtlich falschen Loop |
| M5 | Feldnachweis | 48 h auf Badwolf und BurgCloud; dockerd-CPU und Deskriptoren protokolliert |

## Rückweg

Der Umbau ist additiv (Default-Parameter, neue Schicht). Fällt das Budget aus, ist der Rückweg eine Konfiguration mit sehr großen Kontingenten — nicht ein Ausbau des Codes. Ein Ausschalten des Circuit Breakers ist möglich, muss aber gemeldet werden und darf nicht der Dauerzustand sein.

## Stand 2026-08-26

**SP-1 im Kern erledigt**, 651/651 Tests grün, nichts committet, nichts deployt.

Der Vorfall vom 26.08. ist an der Ursache behoben und durch Tests belegt, die vorher rot waren. Was **offen bleibt** und eine bewusste Entscheidung braucht:

1. **20 Docker-Aufrufstellen laufen weiter außerhalb des Budgets** (siehe WP6.3). Sie sind fixiert und können nur schrumpfen, aber sie sind nicht begrenzt. Die mutierenden darunter brauchen `ExecuteGuardedAsync`, nicht `ExecuteAsync` — sonst bekämen sie eine Wiederholung, die sie nie hatten.
2. **Der Feldnachweis aus M5 fehlt** — 48 h auf Badwolf und BurgCloud mit protokollierter dockerd-CPU und Deskriptorzahl. Das braucht einen Deploy und ist damit deine Entscheidung.
3. **Nur der Log-Monitor markiert seine Zyklen als Hintergrundarbeit.** Die übrigen Loops (Metrics, CVE, Health, ImageUpdate) laufen in der interaktiven Bahn und konkurrieren dort mit der Oberfläche. Ein `BackgroundScope()` je Zyklus, ansonsten mechanisch.
4. **`BackupServiceTests.Validate_accepts_an_equal_or_older_schema`** fiel einmalig in einem von fünf Läufen aus, danach in allen Folgeläufen grün. Als möglicher Flake festgehalten, nicht behoben.

## Definition of Done

- [ ] WP1–WP6 umgesetzt
- [ ] WP6.1/WP6.2 dokumentiert **vorher rot, nachher grün**
- [ ] Reproduktion aus dem Vorfallsbericht läuft ohne Lastanstieg (Deskriptoren ≤ 1, Dauer konstant)
- [ ] Architekturtest WP6.3 in der CI
- [ ] Feldnachweis über 48 h auf zwei realen Servern, Messwerte im Commit oder CHANGELOG festgehalten
- [ ] Jede Circuit-Öffnung erzeugt genau eine Meldung (kein Sturm, kein Schweigen)
- [ ] DI-Boot-Gate in beiden Auth-Modi grün
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
