# PRD-0008: Kubernetes-Parität (GAP-1)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber gemischter Flotten (Docker + k3s), Betreiber, die von Docker auf Kubernetes wechseln
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — GAP-1
- **Ersetzt:** —

## Problem / Motivation

Whiskers kann Kubernetes-Server aufnehmen, Pods auflisten, Logs holen und ehrlich abgebildete Start/Stop/Restart-Operationen ausführen (Scale 0/1, Rollout-Restart). Alles darüber hinaus fehlt — und zwar genau das, was Whiskers ausmacht:

| Funktion | Auf Docker | Auf Kubernetes | Ursache |
|---|---|---|---|
| Log-Alarme | ✅ | ❌ | `LogMonitorService` iteriert `ListAllContainersDetailedAsync`, das K8s-Server herausfiltert |
| Health-/Restart-Loop-Alarme | ✅ | ❌ | `ContainerHealthMonitor`, derselbe Pfad |
| Metriken | ✅ | ❌ | `MetricsSourceDispatcher` überspringt K8s; `SupportsStats: false` |
| CVE-Scanning | ✅ | ❌ | `CveMonitorService` überspringt K8s |
| MCP-Tools / Agent | ✅ | ❌ | keine K8s-Abdeckung |
| Interaktives Exec | ✅ | ❌ | Track B.3 offen |

**Praktische Folge:** Wer seine Flotte auf Kubernetes umstellt, behält von Whiskers einen Pod-Betrachter mit Start/Stop. Alle Zähne bleiben auf der Docker-Seite.

Zweiter Befund: Der `KubernetesWorkloadProvider` wurde nie gegen einen echten Cluster verifiziert (kein Cluster auf der Entwicklungsmaschine). Auch das Helm-Chart für Whiskers *auf* Kubernetes ist gebaut, aber der kind/k3s-Livetest steht laut Definition of Done noch aus.

Dritter Befund, strategisch: Auf Kubernetes ist die Konkurrenz deutlich stärker (Headlamp als CNCF-Projekt, Rancher, k9s, Portainer mit Docker **und** K8s), und der Cluster heilt sich in Teilen selbst — Restarts, Probes, HPA. Ein Nachbau eines Kubernetes-Dashboards ist ein Wettlauf, der nicht zu gewinnen ist.

## Ziele

- Auf einem Kubernetes-Server funktionieren dieselben Kernfunktionen wie auf einem Docker-Server: Alarme, Metriken, Bildscan, Agentenzugriff.
- Die Signale sind Kubernetes-nativ gedacht, nicht aus Docker übersetzt.
- Der Provider ist gegen einen echten Cluster belegt, nicht nur gegen Unit-Tests.

## Non-Goals

- **Kein** Kubernetes-Dashboard mit Ressourcen-Editor, YAML-Bearbeitung, Helm-Verwaltung oder Cluster-Provisionierung. Diesen Vergleich verliert Whiskers gegen Headlamp und Rancher, und er ist nicht die Position, um die es geht.
- **Keine** Multi-Cluster-Föderation oder cluster-übergreifende RBAC-Verwaltung.
- **Keine** Docker-only-Funktionen auf K8s nachbauen: Compose, Host-Shell, Netzwerke, Volume-Backups bleiben ausgeblendet (`WorkloadCapabilities`).
- **Keine** Ablösung der Docker-Pfade. Beide Backends laufen dauerhaft nebeneinander.

## Zielgruppen / Personas

### Betreiber gemischter Flotten (Hauptzielgruppe)

- Kontext: einige Docker-Hosts, ein k3s-Cluster.
- Pain Point: Braucht heute zwei Werkzeuge und hat zwei Alarmwege.

### Betreiber im Umstieg

- Kontext: verschiebt Dienste schrittweise nach k3s.
- Pain Point: Verliert Funktion für jeden verschobenen Dienst — der Umstieg verschlechtert die Beobachtbarkeit.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Log-Alarme laufen über den Workload-Seam und erfassen Kubernetes-Pods mit denselben Regeln wie Docker-Container. | Must |
| FR-02 | Health-Signale werden Kubernetes-nativ erhoben: `CrashLoopBackOff`, `OOMKilled`, `ImagePullBackOff`, fehlgeschlagene Readiness-Probes, Pod-Evictions — nicht nur „Restart-Zähler". | Must |
| FR-03 | Metriken über `metrics-server`, sofern vorhanden; fehlt er, zeigt die Oberfläche einen Leerzustand mit Einrichtungshinweis statt „0 %". | Must |
| FR-04 | CVE-Scanning über die Image-Referenzen der Pods (Registry-basiert), ohne Host-Shell auf den Knoten. | Must |
| FR-05 | Kubernetes-Ereignisse (`Events`) werden als Signalquelle ausgewertet — das K8s-Gegenstück zum Docker-Events-Stream. | Should |
| FR-06 | MCP-Tools decken die Seam-Operationen ab (auflisten, Details, Logs, skalieren, Rollout-Restart) und melden Docker-only-Tools auf K8s-Servern ehrlich als nicht anwendbar. | Must |
| FR-07 | Interaktives Exec in einen Pod (WebSocket → xterm), analog zum Docker-Terminal. | Should |
| FR-08 | Alle Loops laufen auf K8s unter demselben Lastbudget wie auf Docker (SP-1), mit eigenen Grenzwerten für die API-Server-Last. | Must |
| FR-09 | Das RBAC-Manifest deckt exakt die genutzten Rechte ab — nicht mehr — und wird bei jeder neuen Operation mit fortgeschrieben. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** Präzisierung zu FR-06: Die Seam-Operationen sind lesend und schreibend (`scale_workload`, `rollout_restart`) abgedeckt, mit Stufen nach PRD-0013; Docker-only-Werkzeuge antworten auf K8s-Servern mit Begründung statt mit Fehlern. | Must |

## Nicht-Funktionale Anforderungen

- **Kein API-Server-Hammer:** Der Kubernetes-API-Server ist empfindlicher als dockerd. Watches statt Polling, wo möglich; Listen paginiert; alles unter dem Budget aus SP-1.
- **Ehrliche Leerzustände:** Jede nicht unterstützte Funktion zeigt, *warum* sie fehlt, statt leer oder mit Nullwerten zu erscheinen.
- **Namespace-Grenzen respektieren:** Die konfigurierte Namespace-Auswahl gilt für **alle** Loops, nicht nur für die Anzeige.
- **Cluster-Belege statt Unit-Tests:** Jede Anforderung braucht mindestens einen Nachweis gegen kind **und** k3s.

## User Stories

- **US-01:** Als Betreiber einer gemischten Flotte möchte ich eine Log-Alarmregel schreiben, die auf Docker-Containern und K8s-Pods gleichermaßen gilt.
- **US-02:** Als Betreiber möchte ich über einen `CrashLoopBackOff` informiert werden wie über einen Docker-Restart-Loop.
- **US-03:** Als Betreiber möchte ich den Agenten fragen können, warum ein Pod nicht startet.

### Flow für US-02

```
Given ein Pod geht in CrashLoopBackOff
When der Health-Loop den Cluster abfragt
Then erscheint eine Meldung mit Pod, Namespace, Grund, Neustartzahl und letztem Exit-Code,
     in derselben Form wie ein Docker-Restart-Loop-Alarm
```

## Akzeptanzkriterien

- FR-01 bis FR-04, FR-06, FR-08, FR-09 umgesetzt.
- **Livetest-Matrix:** jede Anforderung gegen kind **und** k3s belegt, mit protokolliertem Ergebnis. Ohne diesen Beleg gilt das Paket nicht als fertig — Unit-Tests genügen hier ausdrücklich nicht.
- Ein Cluster mit 50 Pods erzeugt über eine Stunde weniger API-Server-Last als ein `kubectl get pods -w`-Dauerabruf (Vergleichsmessung).
- Ein Cluster ohne `metrics-server` zeigt Leerzustände mit Hinweis, keine Nullwerte und keine Fehlermeldungen.
- Das RBAC-Manifest reicht aus: Ein ServiceAccount mit genau diesem Manifest kann alle Funktionen ausführen — geprüft durch einen Lauf ohne Cluster-Admin-Rechte.
- MCP: Der Agent beantwortet „warum startet Pod X nicht?“ an einem echten Cluster ausschließlich über MCP-Werkzeuge.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| API-Server-Last | `apiserver_request_total`-Rate im Cluster | vergleichbar mit einem Watch | Vielfaches ⇒ Polling statt Watch, der Cluster zahlt |
| Regelabdeckung | dieselbe Log-Regel auf Docker und K8s | beide melden | nur Docker ⇒ der Seam wird umgangen |
| Fehlende Rechte | `kubectl auth can-i --list` mit dem ServiceAccount gegen die genutzten Operationen | deckungsgleich | Whiskers braucht mehr ⇒ RBAC-Manifest ist unehrlich |
| Leerzustand `metrics-server` | Cluster ohne metrics-server | Hinweis sichtbar | „0 %" ⇒ falsche Aussage, gefährlicher als eine Lücke |
| Namespace-Grenze | Pod außerhalb der Auswahl erzeugen | wird ignoriert | erscheint ⇒ Grenzverletzung, Datenschutz- und Rechteproblem |
| `last_success_timestamp` je K8s-Loop | SP-3-Metriken | frisch | alt ⇒ der Loop läuft für K8s gar nicht, sieht aber ruhig aus |

## Woran ich sehe, dass es bricht

1. **Der lautlose Nicht-Lauf.** Der heutige Zustand ist genau dieser Fehler: Loops überspringen K8s-Server, und in der Oberfläche sieht das aus wie „keine Befunde". Nach der Umsetzung ist derselbe Fehler wieder der wahrscheinlichste. **Messstelle:** `last_success_timestamp` je Loop **je Server**, mit K8s-Servern explizit in der Liste. Fehlt der Server dort ganz, läuft der Loop für ihn nicht — und das ist kein Zustand, den man an Alarmen erkennen kann.
2. **Übersetzte statt native Signale.** Wer `RestartCount` von Pods liest und daraus einen Docker-Restart-Loop-Alarm baut, meldet Kubernetes-Normalverhalten als Störung (Rollouts, Evictions, Knotenwartung) und übersieht die echten Fälle. **Messstelle:** Fehlalarmquote nach einem geplanten Rollout — ein normales `kubectl rollout restart` darf **keine** Meldung erzeugen.
3. **Rechte, die im Betrieb fehlen.** Ein Cluster-Admin-Kubeconfig in der Entwicklung verdeckt jede RBAC-Lücke. Beim Nutzer mit dem mitgelieferten Manifest schlägt dann eine Operation fehl — oft still, in einem Hintergrund-Loop. **Gegenprobe:** die gesamte Testmatrix läuft mit dem ausgelieferten ServiceAccount, nie mit Admin-Rechten.
4. **Last, die im Cluster ankommt.** Der API-Server ist die empfindlichste Komponente eines k3s-Clusters auf kleiner Hardware. Ein Polling-Loop über alle Namespaces bringt ihn schneller in Bedrängnis als dockerd. **Messstelle:** `apiserver_request_total` und die etcd-Latenz während eines Whiskers-Zyklus. Steigt die etcd-Latenz messbar, ist der Zugriff falsch gebaut — das ist der GAP-1-eigene Wiedergänger des Vorfalls vom 26.08.
5. **Der Beleg, der fehlt.** Die gefährlichste Eigenschaft dieses Pakets ist, dass es sich vollständig ohne Cluster entwickeln lässt und dabei plausibel aussieht — der aktuelle Provider ist der Beweis. **Konsequenz:** kein Merge ohne protokollierten Lauf gegen kind und k3s.

## Do's

- **SP-1 bis SP-4 zuerst.** Wer die Loops vorher portiert, portiert die Ratsche und das fehlende Budget auf eine empfindlichere API.
- **Watches statt Polling**, wo die Kubernetes-API es anbietet.
- **Native Signale** (`Events`, Conditions, Phasen), keine Docker-Analogien.
- **Testmatrix kind + k3s** ab dem ersten Arbeitspaket, nicht am Ende.
- **Bei jeder neuen Operation das RBAC-Manifest mit fortschreiben** — im selben Commit.

## Don'ts

- **Nicht** versuchen, ein Kubernetes-Dashboard zu werden. Ressourcen-Editor, YAML-Editor, Helm-Verwaltung sind ausdrücklich Non-Goals.
- **Nicht** `SupportsStats: true` setzen, bevor `metrics-server` sauber erkannt und der Leerzustand gebaut ist.
- **Nicht** Cluster-Admin-Rechte im Onboarding verlangen, weil es einfacher ist.
- **Nicht** die Namespace-Auswahl nur in der Anzeige anwenden.
- **Nicht** DaemonSets „starten" oder bare Pods „stoppen" wollen — die ehrlichen Ablehnungen aus B.2 bleiben.

## Abhängigkeiten

- **Wird blockiert von:** SP-1, SP-2, SP-3, SP-4 (die Loops müssen repariert sein, bevor sie portiert werden).
- **Verwandt:** kubernetesImplement Track B.3 (Teilmenge), Track A DoD (kind/k3s-Livetest — dieselbe Testumgebung nutzen).

## Offene Fragen

- **F-01:** CVE-Scanning ohne Host-Shell — Registry-basiert über Trivy im Remote-Modus oder ein Job im Cluster? Vorschlag: Registry-basiert, weil es keine Rechte im Cluster braucht.
- **F-02:** Lohnt sich GAP-1 überhaupt, wenn die eigene Flotte auf Kubernetes wechselt und damit kein Docker-Dogfood mehr existiert? Das ist eine Produktentscheidung, keine technische — siehe Diskussion in [hardeningAndParity.md](../roadmap/hardeningAndParity.md).
