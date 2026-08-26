# Plan-0008: Kubernetes-Parität (GAP-1)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0008](../prd/0008-kubernetes-paritaet.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Auf einem Kubernetes-Server bleibt heute ein Pod-Betrachter mit Start/Stop übrig. Vier Loops überspringen K8s-Server bewusst (`ContainerOperations.cs:109`, `SystemInfoOperations.cs:144`, `MetricsSourceDispatcher.cs:43`, `CveMonitorService.cs:148`), MCP hat keine Abdeckung, und der Provider wurde nie gegen einen echten Cluster verifiziert.

Zwei Randbedingungen prägen diesen Plan:

- **Reihenfolge:** SP-1 bis SP-4 zuerst. Wer die Loops vorher portiert, portiert die Wasserzeichen-Ratsche und das fehlende Lastbudget auf eine API, die weniger verzeiht als dockerd.
- **Beleg:** Dieses Paket lässt sich vollständig ohne Cluster entwickeln und sieht dabei plausibel aus — der heutige Provider ist der Beweis. Deshalb ist die Cluster-Testmatrix Teil jedes Arbeitspakets, nicht ein Abschluss-Schritt.

## Ziele

- Alarme, Metriken, Bildscan und Agentenzugriff funktionieren auf Kubernetes.
- Die Signale sind Kubernetes-nativ, nicht aus Docker übersetzt.
- Jede Anforderung ist gegen kind **und** k3s belegt.

## Arbeitspakete

### WP0: Testumgebung

**Zweck:** Ohne Cluster ist jedes Ergebnis dieses Plans unbelegt.
**Schätzung:** S (1 Tag). **Zuerst.**

1. **WP0.1:** kind-Cluster lokal, k3s-Cluster auf einer kleinen VM — beide reproduzierbar aufgesetzt (Skript im Repo).
2. **WP0.2:** ServiceAccount aus `deploy/k8s/` einrichten; **alle** folgenden Arbeiten laufen mit diesem Konto, nie mit Cluster-Admin.
3. **WP0.3:** Lastmessung vorbereiten: `apiserver_request_total` und etcd-Latenz abgreifbar machen.
4. **WP0.4:** Denselben Aufbau für den offenen kind-Smoke-Test aus `kubernetesImplement.md` Track A nutzen — eine Umgebung, zwei Zwecke.

**Ergebnis:** Ein Prüfstand, auf dem Behauptungen fallen können.

### WP1: Loops auf den Workload-Seam

**Zweck:** Die vier Überspringen-Stellen auflösen.
**Schätzung:** L (5–7 Tage).

1. **WP1.1:** `LogMonitorService` von `ListAllContainersDetailedAsync` auf den Workload-Seam umstellen, backend-neutral.
2. **WP1.2:** `ContainerHealthMonitor` ebenso.
3. **WP1.3:** Metrik-Erhebung über `metrics-server`; fehlt er, Leerzustand mit Einrichtungshinweis — **niemals** Nullwerte.
4. **WP1.4:** CVE-Scan über die Image-Referenzen der Pods, Registry-basiert, ohne Host-Shell auf den Knoten.
5. **WP1.5:** Alle vier laufen unter dem Budget aus SP-1, mit eigenen Grenzwerten für die API-Server-Last.

**Ergebnis:** Dieselben Regeln greifen auf beiden Backends.

**Abnahme:** Dieselbe Log-Regel meldet auf einem Docker-Host und in einem Cluster; `whiskers_self_last_success_timestamp` ist für K8s-Server frisch.

### WP2: Kubernetes-native Signale

**Zweck:** Nicht Docker-Analogien übersetzen, sondern die Wahrheit des Clusters lesen.
**Schätzung:** M (3 Tage).

1. **WP2.1:** `CrashLoopBackOff`, `OOMKilled`, `ImagePullBackOff`, fehlgeschlagene Readiness-Probes, Evictions als eigene Signalarten.
2. **WP2.2:** Kubernetes-`Events` als Quelle auswerten (Watch, nicht Polling).
3. **WP2.3:** **Normalverhalten ausschließen:** Rollouts, geplante Evictions und Knotenwartung dürfen keine Störungsmeldung erzeugen.
4. **WP2.4:** Namespace-Auswahl gilt für alle Loops, nicht nur für die Anzeige.

**Abnahme:** Ein `kubectl rollout restart` erzeugt **keine** Meldung; ein echter `CrashLoopBackOff` erzeugt genau eine.

### WP3: MCP und Agent

**Zweck:** Die Zähne auch auf Kubernetes.
**Schätzung:** M (3 Tage).

1. **WP3.1:** MCP-Werkzeuge für die Seam-Operationen: auflisten, Details, Logs, skalieren, Rollout-Restart.
2. **WP3.2:** Docker-only-Werkzeuge melden auf K8s-Servern ehrlich, dass sie nicht anwendbar sind — statt zu scheitern oder Leeres zu liefern.
3. **WP3.3:** Guardrail-Stufen für die K8s-Operationen festlegen (Skalieren auf 0 ist ein Eingriff, kein Lesevorgang).
4. **WP3.4:** Werkzeugsichtbarkeit an `WorkloadCapabilities` koppeln, nicht an Fallunterscheidungen im Code.

**Abnahme:** Der Agent beantwortet „warum startet Pod X nicht?" auf einem echten Cluster mit den richtigen Werkzeugen.

### WP4: Interaktives Exec

**Zweck:** Der letzte offene Punkt aus Track B.3.
**Schätzung:** M (2 Tage).

1. **WP4.1:** Pod-Exec über WebSocket, angebunden an das vorhandene xterm-Frontend.
2. **WP4.2:** Rechte und Audit-Verhalten identisch zum Docker-Terminal.

**Abnahme:** Interaktive Sitzung in einen Pod, Abbruch sauber, Audit-Eintrag vorhanden.

### WP5: RBAC ehrlich halten

**Zweck:** Das mitgelieferte Manifest muss genügen — und darf nicht mehr verlangen als nötig.
**Schätzung:** S (1 Tag, begleitend).

1. **WP5.1:** Bei **jeder** neuen Operation das Manifest im selben Commit fortschreiben.
2. **WP5.2:** Abgleich `kubectl auth can-i --list` gegen die tatsächlich genutzten Operationen.
3. **WP5.3:** Testlauf der gesamten Matrix mit genau diesem ServiceAccount.

**Abnahme:** Vollständiger Funktionslauf ohne Cluster-Admin-Rechte.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: Seam-Werkzeuge, `scale_workload`, `rollout_restart` — K8s-Operationen über den Workload-Seam. Stufe: read / write, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Docker-only-Werkzeuge antworten auf K8s-Servern mit Begründung statt Fehler. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
SP-1..SP-4 (extern, zwingend) ──> WP0 ──> WP1 ──> WP2 ──> WP3 ──> WP4
                                            └────> WP5 (begleitend ab WP1)
```

- **Extern blockiert von:** SP-1, SP-2, SP-3, SP-4.
- **Teilt die Testumgebung mit:** `kubernetesImplement.md` Track A (offener kind/k3s-Smoke-Test).

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| API-Server-Last | `apiserver_request_total`-Rate | vergleichbar mit einem Watch |
| etcd-Latenz während eines Zyklus | Cluster-Metriken | unverändert |
| `last_success_timestamp` je K8s-Loop | Selbstmetriken (SP-3) | frisch, nie fehlend |
| Fehlalarme nach Rollout | Alarm-Historie | null |
| Rechteabgleich | `kubectl auth can-i --list` | deckungsgleich mit dem Manifest |
| Leerzustand ohne metrics-server | Oberfläche | Hinweis, keine Nullwerte |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Lautloser Nicht-Lauf | K8s-Server erscheint ruhig, wird aber nicht geprüft | `last_success_timestamp` **je Server**, K8s-Server explizit in der Liste (SP-3 WP2.3) |
| Übersetzte statt native Signale | Rollouts erzeugen Fehlalarme, echte Fälle fehlen | WP2.3 mit Rollout-Gegenprobe |
| Entwicklung mit Admin-Rechten | RBAC-Lücken fallen erst beim Nutzer auf | WP0.2 — Admin-Rechte sind im ganzen Plan verboten |
| API-Server überlastet | der Vorfall vom 26.08. auf Kubernetes | Watches statt Polling; etcd-Latenz als Messstelle |
| Entwicklung ohne Cluster | plausibel aussehender, unbelegter Code | WP0 zuerst; kein Merge ohne protokollierten kind- **und** k3s-Lauf |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP0 | beide Cluster reproduzierbar, ServiceAccount aktiv |
| M2 | WP1 | dieselbe Log-Regel meldet auf Docker und K8s |
| M3 | WP2 | Rollout schweigt, CrashLoopBackOff meldet |
| M4 | WP3 + WP4 | Agent beantwortet eine echte Frage am Cluster; Exec funktioniert |
| M5 | WP5 + Abschluss | vollständige Matrix mit dem ausgelieferten ServiceAccount, protokolliert |

## Rückweg

Die Loops bleiben backend-neutral; ein Rückfall auf „K8s überspringen" ist eine Konfiguration, kein Codeausbau. Erweist sich die API-Last als zu hoch, werden Intervalle je Backend getrennt konfiguriert, bevor Funktionen entfallen.

## Definition of Done

- [ ] WP0–WP5 umgesetzt
- [ ] **Testmatrix gegen kind und k3s protokolliert** — Unit-Tests gelten hier ausdrücklich nicht als Nachweis
- [ ] Vollständiger Funktionslauf mit dem ausgelieferten ServiceAccount, ohne Cluster-Admin
- [ ] `kubectl rollout restart` erzeugt keine Meldung
- [ ] API-Server-Last vergleichbar mit einem Watch; etcd-Latenz unverändert
- [ ] Cluster ohne metrics-server zeigt Leerzustand mit Hinweis
- [ ] Alle K8s-Server erscheinen in allen Loop-Kennzahlen
- [ ] RBAC-Manifest deckt genau die genutzten Rechte ab
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
