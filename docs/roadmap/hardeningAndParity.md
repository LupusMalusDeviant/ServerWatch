# hardeningAndParity.md — Selbstschutz und Wettbewerbsparität

> **Ziel:** Zwei Stränge, die aus derselben Analyse stammen und deshalb in einem Dokument stehen:
> - **Strang SP (Selbstschutz):** Whiskers darf die Server, die es überwacht, nicht beschädigen — und muss merken, wenn es doch passiert. Auslöser ist [der Vorfall vom 26.08.2026](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md).
> - **Strang GAP (Parität):** Die Punkte, an denen Whiskers im direkten Vergleich mit Portainer, Coolify, Netdata, Uptime Kuma und den K8s-Werkzeugen heute verliert.
>
> **Gemeinsamer Nenner beider Stränge und der Grund, warum sie zusammengehören:** Whiskers misst viel und bewertet fast nichts. Der Vorfall wurde 1.600-mal am Tag gemessen und nie gemeldet; das Angriffsszenario in [attackResponse.md](attackResponse.md) scheitert an derselben unterbrochenen Kette. Der Satz aus dem Vorfallsbericht ist die kürzeste Fassung: *alle vorhandenen Schutzmaßnahmen schützen Whiskers vor dem Server, keine schützt den Server vor Whiskers.*
>
> **Aufwand:** SP ≈ 12–18 Arbeitstage, GAP ≈ 25–40 (GAP-1 und GAP-4 dominieren). **Risiko:** SP-1 fasst die Docker-Aufrufkette an, die jeder Loop benutzt — das ist der invasivste Eingriff im ganzen Dokument.

---

## 1. Warum das vor dem Marketing kommt

`beatPortainerCoolify.md` Phase 0 nennt i18n und Screenshots als Launch-Blocker. Das stimmt nicht mehr. Seit dem 26.08. existiert ein schriftlich dokumentierter Fall, in dem Whiskers einen 2-Kern-Server sechs Tage lang auf 98 % CPU gefahren hat. Das ist genau die Geschichte, die eine Adoption in `r/selfhosted` beendet — und sie ist wahr und öffentlich nachlesbar, sobald das Repo Aufmerksamkeit bekommt.

**SP-1 und SP-2 sind damit Launch-Voraussetzung, nicht Feinschliff.** Sie gehören in `beatPortainerCoolify.md` Phase 0 vor L1/L2.

Der Umkehrschluss ist die Chance: „Das Werkzeug, das seine eigene Last budgetiert und meldet, wenn es dem überwachten Server schadet" ist eine Aussage, die kein Wettbewerber trifft. Der Vorfall ehrlich behandelt ist ein Vertrauensgewinn, verschwiegen ein Totalschaden.

---

## 2. Strang SP — Selbstschutz

| # | Paket | Kern | PRD | Plan |
|---|---|---|---|---|
| SP-1 | Abbruch & Lastbudget | `CancellationToken` durch die Docker-Kette; ein geteiltes Lastbudget je Server für **alle** Loops; Circuit Breaker; Lastinvarianten in CI | [PRD-0001](../prd/0001-abbruch-und-lastbudget.md) | [Plan-0001](../plans/0001-abbruch-und-lastbudget.md) |
| SP-2 | Fensterdeckel & Aussperrung | Wasserzeichen-Ratsche entschärfen; Container nach n Fehlschlägen mit Backoff aussperren — **und das melden** | [PRD-0002](../prd/0002-fensterdeckel-und-aussperrung.md) | [Plan-0002](../plans/0002-fensterdeckel-und-aussperrung.md) |
| SP-3 | Selbstbeobachtung | `self:`-Metriken (Call-Rate, Latenz, in-flight, Zyklusdauer, Timeouts), auf `/metrics` exportiert; Aktions-Zeitachse über den Metriken | [PRD-0003](../prd/0003-selbstbeobachtung.md) | [Plan-0003](../plans/0003-selbstbeobachtung.md) |
| SP-4 | Host- & Baseline-Alarme | Host-CPU/RAM analog `disk:{server}`; „Last, die kein Container erklärt"; rollende Baseline statt fester Schwelle | [PRD-0004](../prd/0004-host-und-baseline-alarme.md) | [Plan-0004](../plans/0004-host-und-baseline-alarme.md) |
| SP-5 | Not-Aus | Hintergrund-Loops pausieren — manuell je Server/global, automatisch bei offenem Circuit | [PRD-0005](../prd/0005-not-aus.md) | [Plan-0005](../plans/0005-not-aus.md) |
| SP-6 | Wirkungskontrolle | Jede automatische Aktion wird auf Wirkung geprüft und bei Ausbleiben zurückgenommen | [PRD-0006](../prd/0006-wirkungskontrolle.md) | [Plan-0006](../plans/0006-wirkungskontrolle.md) |
| SP-7 | Hygiene-Inventar | Container ohne Log-Rotation melden, **bevor** sie zünden; Docker-Proxy-Container aus dem Log-Scan nehmen | [PRD-0007](../prd/0007-hygiene-inventar.md) | [Plan-0007](../plans/0007-hygiene-inventar.md) |

### Abhängigkeiten SP

```
SP-1 (Abbruch + Budget) ──┬──> SP-2 (Deckel + Aussperrung)
                          ├──> SP-5 (Not-Aus nutzt denselben Circuit)
                          └──> SP-3 (Budget-Zähler SIND die self:-Metriken)
SP-3 ──> SP-4 (Baseline braucht die Zeitreihen)
SP-4 ──> SP-6 (Wirkungskontrolle misst gegen dieselben Signale)
SP-7  unabhängig, jederzeit
attackResponse AR-1 (Incident-Objekt) ──> SP-2/SP-4/SP-5 melden dort hinein
```

> **Ohne SP-1 ist jede Drosselung wirkungslos.** Der Vorfallsbericht sagt es unmissverständlich: solange ein abgelaufener Aufruf serverseitig weiterläuft, entlastet keine Pause und kein Backoff den Server. SP-1 zuerst, ohne Ausnahme.

---

## 3. Strang GAP — Wettbewerbsparität

| # | Paket | Verlorener Vergleichspunkt | PRD | Plan |
|---|---|---|---|---|
| GAP-1 | Kubernetes-Parität | Auf einem K8s-Server bleibt heute ein Pod-Viewer mit Start/Stop übrig — Alarme, Metriken, CVE, MCP: alles Docker-only | [PRD-0008](../prd/0008-kubernetes-paritaet.md) | [Plan-0008](../plans/0008-kubernetes-paritaet.md) |
| GAP-2 | Externe Checks & Status-Seite | Uptime Kuma: 90+ Kanäle, synthetische Checks, Status-Seiten. Whiskers: 9 Kanäle, nur Innensicht | [PRD-0009](../prd/0009-externe-checks-und-statusseite.md) | [Plan-0009](../plans/0009-externe-checks-und-statusseite.md) |
| GAP-3 | Git-Deploy-Ausbau | Coolify: 280+ Ein-Klick-Dienste, Domains/TLS integriert. Whiskers F5: https-only, v1 | [PRD-0010](../prd/0010-git-deploy-ausbau.md) | [Plan-0010](../plans/0010-git-deploy-ausbau.md) |
| GAP-4 | Hochverfügbarkeit | Single-Replica by design; Blazor-Circuits, 8+ Loops ohne Leader-Election, JSON-Stores mit Prozess-Cache | [PRD-0011](../prd/0011-hochverfuegbarkeit.md) | [Plan-0011](../plans/0011-hochverfuegbarkeit.md) |
| GAP-5 | Reife & Vertrauen | Ein Entwickler, v0.13.x, gegen 10 Jahre Portainer und 50.000 Coolify-Sterne | [PRD-0012](../prd/0012-reife-und-vertrauen.md) | [Plan-0012](../plans/0012-reife-und-vertrauen.md) |

### Abhängigkeiten GAP

```
SP-1..SP-4  ──> GAP-1 (K8s erbt die Loops — erst reparieren, dann portieren)
SP-3        ──> GAP-4 (ohne Selbstbeobachtung ist Multi-Replica nicht debuggbar)
stableDB ✅ + changeme C7 (Stores→DB) ──> GAP-4
GAP-2  unabhängig
GAP-3  unabhängig
GAP-5  läuft durchgehend nebenher
```

> **Die Reihenfolge SP vor GAP-1 ist keine Vorliebe.** Wer die Loops zuerst auf Kubernetes portiert, portiert die Wasserzeichen-Ratsche und das fehlende Lastbudget gleich mit — auf eine API, die noch weniger verzeiht als dockerd.

---

## 3a. Querschnitt MCP — jedes Paket bringt seine Agenten-Oberfläche mit

| # | Paket | Kern | PRD | Plan |
|---|---|---|---|---|
| MCP | MCP- und Agenten-Oberfläche | Stufe am Werkzeug statt in einer Liste nebenan; Registrierungstest je Modul; Katalog-Momentaufnahme; die sieben stummen Module nachrüsten | [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) | [Plan-0013](../plans/0013-mcp-und-agentenoberflaeche.md) |

Die Positionierung von Whiskers ist **regierte Autonomie** — der Agent lebt im Produkt, nicht als Adapter davor. Diese Aussage hält nur, solange die MCP-Oberfläche mitwächst. Jedes Paket, das neue Signale erzeugt oder neue Eingriffe ermöglicht, ohne sie über MCP verfügbar zu machen, macht den Agenten für genau den Bereich blind, den es gerade gebaut hat.

Der Ist-Stand zeigt, dass das kein theoretisches Risiko ist:

- **Sieben Module liefern heute null Werkzeuge** (`GitDeploy`, `Deployment`, `HostManagement`, `ImageUpdate`, `VolumeBackups`, `Notifications`, `Terminal`). Der Agent kann weder deployen noch Images aktualisieren noch Sicherungen anstoßen.
- **Ein Werkzeug ohne Eintrag in `DefaultToolLevels` wird stillschweigend admin-only** (`McpPermissionCheck.cs:31` fällt auf `admin` zurück) — registriert, in `tools/list` sichtbar, für den Agenten mit `write`-Obergrenze aber **immer** abgelehnt.
- **Der Registrierungstest prüft nur eine Untergrenze** (`count > 40`). Fällt ein Modul aus dem Katalog, bleibt er grün — nachdem genau diese Fehlerklasse den ausgelieferten MCP-Server von 0.12.0 bis 0.13.0 auf **null** Werkzeuge gesetzt hat, über mehrere Releases unbemerkt.

Das ist dasselbe Muster wie im Vorfall vom 26.08.: geliefert, registriert, nie bewertet.

> **Plan-0013 WP1–WP3 laufen vor allen anderen Paketen.** Sie legen die Form fest, in der Werkzeuge entstehen. Danach entsteht jedes Paketwerkzeug direkt richtig; umgekehrt müssten zwölf Pakete nachträglich umgebaut werden.

Jedes Paket-PRD trägt dafür eine `FR-MCP`-Zeile, jeder Plan ein `WP-MCP`-Arbeitspaket und eine DoD-Zeile. Der Sollzustand je Paket steht in [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) §„Werkzeuge je Paket".

---

## 4. Nicht verhandelbare Regeln für beide Stränge

**Jede Selbstdrosselung wird gemeldet.** Der Satz stammt aus dem Vorfallsbericht und gilt für jedes Paket in Strang SP. Eine stille Schutzmaßnahme verwandelt „leise" in „blind" — und versteckt den nächsten Vorfall hinter der Maßnahme gegen den letzten.

**Kein Signal ohne Gegenprobe.** Jedes neue Alarm-Signal braucht einen dokumentierten Weg, es absichtlich auszulösen. Ein Alarm, dessen Auslösung nie beobachtet wurde, ist eine Vermutung.

**Kein Paket gilt als fertig, weil es läuft.** Jeder PRD und jeder Plan in diesem Strang trägt einen Abschnitt *„Woran ich sehe, dass es bricht"*. Ein grüner Testlauf beweist, dass der Fall funktioniert, den jemand bedacht hat. Gebraucht wird die Messstelle, die im Betrieb anschlägt, wenn genau das nicht mehr stimmt.

**Nichts an fremden Systemen automatisch verändern.** Stufe 0 (sich selbst zurücknehmen) läuft ohne Rückfrage. Alles, was den überwachten Server verändert, ist Vorschlag oder Freigabe. Begründung im Vorfallsbericht: der Log-Monitor hat den Vorfall in dem Glauben verursacht, sein Timeout schütze bereits.

**Kein Paket ohne MCP-Oberfläche.** Was die Oberfläche zeigt, muss der Agent abfragen können; was sie auslöst, muss er unter Guardrails auslösen können — mit den in [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) benannten Ausnahmen (Zwei-Personen-Freigaben, Entsperrung nach Schutzabschaltung). Ein Paket ohne Werkzeug gilt als nicht fertig, nicht als „Werkzeug kommt später".

---

## 5. Reihenfolge über beide Stränge

| Welle | Inhalt | Warum jetzt |
|---|---|---|
| **0 — Form festlegen** | MCP (Plan-0013 WP1–WP3) | Legt fest, wie Werkzeuge entstehen. Ein halber Tag Vorarbeit, der zwölf spätere Umbauten erspart. |
| **1 — Blutung stoppen** | SP-1, SP-2, SP-7 | Behebt den dokumentierten Vorfall an der Ursache. SP-7 ist billig und nimmt den Auslöser weg. |
| **2 — Sehen lernen** | SP-3, SP-4, SP-5 | Ohne diese Welle merkt niemand den nächsten Vorfall. Danach ist Whiskers erstmals sein eigener Nutzer. |
| **3 — Vertrauen belegen** | SP-6, GAP-5, GAP-2 | Wirkungskontrolle macht Automatik verantwortbar; GAP-5/GAP-2 sind die sichtbaren Vergleichspunkte mit dem besten Aufwand-Nutzen-Verhältnis. |
| **4 — Reichweite** | GAP-1, GAP-3 | Große Pakete, die auf einer reparierten Basis stehen müssen. |
| **5 — Skalierung** | GAP-4 | Erst sinnvoll, wenn Betrieb und Beobachtbarkeit belastbar sind. |

**Kleinster sinnvoller Schnitt: Welle 1.** Danach ist der bekannte Fehler behoben. **Kleinster verantwortbarer Schnitt vor einem Launch: Welle 1 + 2.** Denn erst dann fällt ein Rückfall auf, ohne dass ein Mensch zufällig auf die Übersichtsseite schaut.

---

## 5a. Stand 2026-08-27 und was noch einen Server braucht

**Wellen 0 bis 3 sind im Kern umgesetzt** (835 Tests, nichts deployt). Erledigt: SP-1 (Abbruch, Lastbudget,
Circuit), SP-2, SP-3 (bis auf die Leerlaufmessung), SP-4, SP-5, SP-6 (WP1/WP2/WP5), SP-7, MCP. **GAP-1 bis
GAP-5 sind unberührt** — das sind Wochen, keine Stunden.

### Bewusst verworfen, mit Begründung im jeweiligen Plan

| Punkt | Warum nicht |
|---|---|
| SP-5 WP3 — Auto-Pause bei offenem Circuit | Der Circuit sperrt ohnehin; eine Pause obendrauf nähme die Überwachung weg, wenn der Server wackelt (Nutzerentscheidung) |
| SP-5 WP1.2 — Pause überlebt Neustart | Eine Pause reagiert auf etwas, das gerade passiert; überlebt sie den Neustart, überlebt sie ihren Grund (Nutzerentscheidung) |
| SP-5 WP4.3 — gestaffelter Wiederanlauf | Gemessen: es entsteht kein Sturm. Mechanismus gegen ein Problem, das die vorhandenen Mechanismen ausschließen |
| SP-6 WP3/WP4 — Rücknahme, Wiederholungssperre | Der Plan verlangt selbst vier Wochen Beobachtung zuerst |
| SP-1 — 16 der 24 Docker-Aufrufstellen | Interaktiv oder langlaufend; eine Circuit-Abweisung nähme dem Betreiber die Reparatur im ungünstigsten Moment |
| SP-4 WP1.3 — Schwellen je Server | Die rollende Baseline löst den Regelfall bereits; die verbleibende Ausnahme gibt es in dieser Flotte nicht, und die richtigen Zahlen kennt heute niemand — **erst messen, dann setzen** (Nutzerentscheidung) |

### Abnahmen, die ohne laufenden Server nicht zu erbringen sind

Diese Punkte sind **nicht offen im Sinne von unerledigt** — der Code steht und ist getestet. Was fehlt, ist
die Messung an der Wirklichkeit, und die braucht einen Deploy:

| Paket | Abnahme |
|---|---|
| SP-1 | Feldnachweis über 48 h auf zwei realen Servern (dockerd-CPU, Deskriptorzahl protokolliert) |
| SP-3 | Leerlaufmessung über 30 min mit und ohne Selbstmessung (WP6.1) |
| SP-4 | `stress-ng` auf dem Host → Meldung mit korrekter Ursachenklasse; künstlich verlangsamter Proxy → Latenzmeldung |
| SP-4 | **Fehlalarmquote** — der synthetische Prüfstand beweist, dass die Regeln anschlagen, nicht dass sie in einer normalen Woche schweigen |
| SP-5 | Wirksamkeit des Not-Aus am Zielserver: 0 neue Anfragen binnen 60 s |
| SP-7 | Gemessene Loggröße gegen `du -sh` (< 20 % Abweichung); Behebungsbefehl läuft ohne Nacharbeit |
| alle | `tools/list` am laufenden Server enthält die neuen Werkzeuge mit der erwarteten Stufe |

Der letzte Punkt betrifft alle Pakete gemeinsam und hat einen konkreten Anlass: Von 0.12.0 bis 0.13.0 hat der
MCP-Server **null** Werkzeuge ausgeliefert, und kein Test, kein Log und kein Alarm hat es gesagt. Der
`McpServedSurfaceTests` bootet inzwischen die echte Anwendung und fragt `tools/list` ab — aber die Gegenprobe
am tatsächlich laufenden Server bleibt der einzige Beweis, der zählt.

---

## 6. Querverweise

- [Vorfallsbericht 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) — Ursache, Messwerte, Prüfkriterium
- [attackResponse.md](attackResponse.md) — der Angriffs-Strang; AR-1 (Incident-Objekt) trägt auch die selbstverschuldeten Vorfälle
