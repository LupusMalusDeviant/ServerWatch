# attackResponse.md — Angriffserkennung, Incident-Führung und Dienst-Schutz

> **Ziel:** Whiskers soll einen laufenden Angriff auf einen verwalteten Dienst **erkennen, als Incident führen, beweissicher dokumentieren und die Gegenmaßnahme regieren** — statt wie heute eine einzelne `warning`-Zeile abzusetzen und danach zehn Minuten zu schweigen.
>
> **Produkt-Einordnung:** Das ist der Schritt von „Whiskers meldet" zu „Whiskers schützt". Es ist die logische Fortsetzung der bestehenden Differenzierer (MCP + handelnder Agent mit Guardrails/Approvals, SSH-key-freier Betrieb, CVE-Scanning) und hat bei Portainer/Coolify/Dockge kein Äquivalent. Siehe [product/POSITIONING.md](../product/POSITIONING.md).
>
> **Aufwand:** grob 8–14 Arbeitstage über alle Pakete, sinnvoll in drei Wellen schneidbar. **Risiko:** mittel — zwei additive DB-Migrationen (beide Assemblies!), ein neuer anonymer HTTP-Endpunkt, und mit AR-5 erstmals Aktionen, die Verfügbarkeit kosten können.

---

## 1. Der Auslöser: das Referenzszenario

Grundlage ist ein Container-Log von `burgcloud` (Vereins-Cloud, nginx + ASP.NET-App + MySQL) während eines Credential-Stuffing-Angriffs, 26.08.2026, 17:25–17:33:

- **17:27** — MySQL-Auth-Fehler beginnen; fail2ban greift, bannt Host für Host.
- **17:27:15 → 17:28:58** — ein Sidecar `dbguard` zählt in einem 60-s-Fenster mit: `16/min von 4 Hosts` → `51/11` → `120/12` → `177/30` → `233/31` → `310/41` → `381/45`, dazu `18 distinct usernames, 0 successful auths` → Klassifikation `credential-stuffing`.
- **17:28:25** — `host_cache saturated: 128/128`; **17:28:46** — `fail2ban lag detected: 21 banned, 45 active - blocklist not converging`. Die vorhandene Abwehr kommt nicht mehr hinterher.
- **17:28:44 / 17:28:49** — die App kippt: `Too many connections`, dann `Host '172.19.0.5' is blocked because of many connection errors`. Der Angriff hat den *legitimen* App-Zugang verdrängt.
- **17:28:58 → 17:29:04** — Policy `protect-data-at-rest` greift: geordneter `mysqladmin shutdown`, Restart-Policy auf `no` überschrieben, Lockfile `/var/lib/mysql/.security-lock`, **kein** Self-Heal — Freigabe ist eine Operator-Entscheidung (`dbguard unlock --confirm`).
- **17:29:05 ff.** — Incident-Report als JSON, Top-5-Quellen, Evidenz-Upload, Meldung an Whiskers (`202 Accepted`, `incident=WSK-2026-0826-0431`, `dedupe-key=burgcloud/dbguard/credential-stuffing`), Container als `DOWN (expected=stopped-by-policy)` markiert, Heartbeat alle 60 s mit `state=LOCKED`.

**Das Szenario ist architektonisch richtig geschnitten** — und genau deshalb taugt es als Zielbild: `dbguard` sitzt *neben* mysqld, sieht jede Zeile in Echtzeit und entscheidet in drei Sekunden. Whiskers pollt alle 60 s über SSH. Whiskers kann diese Rolle **nie** übernehmen und soll es auch nicht.

**Der Log dokumentiert seine eigene Lücke.** Zeile `17:29:08`:

```
dbguard.whiskers: log-alert bridge: docker exec burgcloud_app sh -c "echo ... > /proc/1/fd/1"
```

Das ist ein Workaround: der Sidecar schreibt in *fremdes* Container-stdout, damit der Log-Monitor es aufliest — weil es keinen Ingest-Endpunkt gibt. Genau diese Rückkopplung ist außerdem die Sorte Selbst-Verstärkung, die die „Echte Fehler"-Regel schon einmal auf 133 Auslösungen getrieben hat (siehe `SelfContainerNames` in [LogMonitorService.cs:41-52](../../src/Whiskers/Services/LogMonitor/LogMonitorService.cs)).

---

## 2. Ist-Zustand (verifiziert, Stand v0.13.1)

### 2.1 Was da ist

| Baustein | Datei | Stand |
|---|---|---|
| Log-Alarme | [LogMonitorService.cs](../../src/Whiskers/Services/LogMonitor/LogMonitorService.cs) | 60-s-Zyklus (`CheckInterval`, Z. 31), `docker logs --since`, **200 Zeilen Tail-Cap** (`TailLines`, Z. 39), 15-s-Fetch-Timeout, fleet-weit, Selbst-Log-Ausschluss |
| Regel-Modell | [LogAlertRule.cs](../../src/Whiskers.Data/Entities/LogAlertRule.cs) | Pattern (Regex/Plaintext), Severity, `CooldownMinutes` (Default 10), Container-Filter, Notify-Flags |
| Alarm-Historie | `AlertHistoryEntity` in [MetricsDbContext.cs:36-46](../../src/Whiskers.Data/MetricsDbContext.cs), geschrieben in [CompositeNotificationService.cs:78](../../src/Whiskers/Services/Notifications/CompositeNotificationService.cs) | ServerId/Container/AlertType/Message/Timestamp + `bool Resolved` |
| In-App-Feed | [InAppNotificationStore.cs](../../src/Whiskers/Services/Notifications/InAppNotificationStore.cs) | persistiert, Cap 2000, Bell + `/notifications` |
| Kanäle | `Services/Notifications/` | Matrix, Mattermost, Slack, Discord, Telegram, ntfy, E-Mail, Webhook, In-App |
| Autonome Reaktion | [AiTriggerDispatcher.cs](../../src/Whiskers/Services/Agent/Triggers/AiTriggerDispatcher.cs) | Notification-Event → Agentenlauf unter Guardrail-Preset, Principal-Ceiling (Default `write`), Bestätigungen werden **verweigert**, max. 3 parallel, Cooldown je Trigger+Container |
| Guardrails | [BuiltInGuardrailRules.cs](../../src/Whiskers/Services/Agent/Guardrails/BuiltInGuardrailRules.cs) | Principal-Ceiling, Read-Only-Kill-Switch, Tool-Deny-List, Approvals |
| Aktionen | `Mcp/Tools/ContainerTools.cs`, `ServerTools.cs` | start/stop/restart/update/deploy, `execute_command`, Logs, Metriken |
| Firewall | [IFirewallService.cs](../../src/Whiskers/Services/Server/IFirewallService.cs) | ufw: `AddRuleAsync(port, protocol, action, from)`, `RemoveRuleAsync(ruleNumber)`, Status an/aus |
| Inbound HTTP | [WhiskersPipelineExtensions.cs:294](../../src/Whiskers/Startup/WhiskersPipelineExtensions.cs) | **nur** `POST /api/webhooks/{id}` mit HMAC → CI/CD-Aktion |
| Metriken | `Services/Metrics/` | Prometheus/VictoriaMetrics **lesend** (`IPrometheusMetricsSource`), Docker-Stats |
| Wartungsmodus | [IMaintenanceStateService.cs](../../src/Whiskers/Services/Maintenance/IMaintenanceStateService.cs) | 503-Gate für Whiskers **selbst** (Restore-Pfad), nicht für verwaltete Dienste |
| Korrelation | `CorrelationId` auf `NotificationEvent` + `McpToolCall` (WP-05) | Alarm → Agentenlauf → Approval → Aktion als eine Spur |

### 2.2 Warum das für den Referenzfall nicht reicht

**Detektion — strukturell untauglich, nicht nur zu langsam.**

[LogMonitorService.cs:220-238](../../src/Whiskers/Services/LogMonitor/LogMonitorService.cs) bricht beim **ersten** Treffer ab (`break;`, Z. 233), setzt den Cooldown und meldet *eine* Zeile. Bei 513 Fehlversuchen in 120 s produziert allein `burgcloud_mysql` weit mehr als 200 Zeilen pro Minute — der Tail-Cap schneidet ab, der 60-s-Zyklus kommt zu spät, der 10-Minuten-Cooldown verschluckt den Rest. Ergebnis: aus einem Angriff mit 45 Quellen wird **eine** `warning`-Zeile ohne Zähler, ohne Quellenverteilung, ohne Eskalation.

Es gibt zudem **keine metrik-basierten Alarmregeln** — Alarmierung ist ausschließlich Log-Pattern-Matching auf Einzelzeilen. Ein Signal wie „Auth-Fehlerrate pro Minute" existiert im Modell nicht.

**Incident-Führung — es gibt kein Incident-Objekt.**

`AlertHistoryEntity` ist ein Proto-Incident: flache Zeile plus `bool Resolved`, und `Resolved` wird ausschließlich für `server_recovered` gesetzt ([CompositeNotificationService.cs:97-102](../../src/Whiskers/Services/Notifications/CompositeNotificationService.cs)). Es fehlen: stabile Incident-ID, Dedupe-Key, Zustandsautomat (`open/acknowledged/contained/resolved`), `FirstSeen`/`LastSeen`, Ereigniszähler, **Evidenz-Anhänge**. `WSK-2026-0826-0431` lässt sich heute nicht abbilden.

**Ingest — nicht vorhanden.**

`/api/monitoring/events` gibt es nicht. Der einzige anonyme Inbound-Pfad ist der Webhook-Endpunkt, dessen HMAC an *eine* konfigurierte Deploy-Aktion gebunden ist — kein Maschinen-Token für einen meldenden Sidecar, kein Evidenz-Upload, keine `202 + Incident-ID`-Antwort, keine Idempotenz.

**Schutz — zu grob und zu gefährlich für Automatisierung.**

`IFirewallService` kennt ufw-Einzelregeln. 45 Hosts sperren, IPv6-Präfixe aggregieren, nach zwei Stunden automatisch entsperren: alles nicht vorhanden. `RemoveRuleAsync(int ruleNumber)` ist für Automatisierung sogar aktiv **gefährlich** — ufw-Regelnummern verschieben sich nach jedem Löschen, ein automatisierter Unban kann die falsche Regel treffen.

**Der schwerwiegendste Einzelbefund: Whiskers würde gegen den Schutz arbeiten.**

Einen Zustand `expected=stopped-by-policy` gibt es nicht. Ein versiegelter Container ist für [ContainerHealthMonitor.cs](../../src/Whiskers/Services/HealthMonitor/ContainerHealthMonitor.cs) schlicht ein Ausfall: er alarmiert, `ServerReachabilityTracker` meldet Unerreichbarkeit, ein `Restart`-`ScheduledTask` ([TaskExecutor.cs:67](../../src/Whiskers/Services/Scheduler/TaskExecutor.cs)) würde ihn wieder hochfahren, und ein AI-Trigger auf `container_down` könnte dem Agenten genau das nahelegen. **Die Schutzmaßnahme und das Monitoring würden einander bekämpfen.**

---

## 3. Zielarchitektur: zwei Ebenen, klar geschnitten

```
   +--------------------------- Kontrollebene = Whiskers ----------------------------+
   |  Ingest-API  ->  Incident-Store (Zustand, Dedupe, Evidenz)  ->  Korrelation     |
   |       ^                          |                                              |
   |  Heartbeat/Deadman      AiTrigger + Guardrails + Approvals  ->  Governance/Audit |
   |       ^                          |                                              |
   +-------+--------------------------+----------------------------------------------+
           |  (HTTPS, Maschinen-Token)|  (MCP-Tools, Policy-gebunden)
   +-------+--------------------------v----------------------------------------------+
   |  Datenebene = whiskers-guard (Sidecar, neben dem Dienst)                         |
   |  liest lokal in Echtzeit - zaehlt in Fenstern - handelt lokal - meldet nach oben |
   +---------------------------------------------------------------------------------+
```

**Whiskers bleibt Kontrollebene.** Es korreliert, führt Incidents, entscheidet Policy, holt Freigaben, dokumentiert beweissicher. Es ist ausdrücklich **nicht** im Millisekundenpfad.

**`whiskers-guard` wird Datenebene.** Ein schlanker Sidecar neben dem Dienst — genau die Rolle, die das Referenzszenario `dbguard` gibt.

**Fallback ohne Sidecar:** Für Dienste, an die kein Sidecar gestellt werden kann, muss der bestehende Log-Pfad wenigstens vom Polling auf **Streaming** umgestellt werden (AR-2). Poll bleibt Rückfallebene.

---

## 3a. Querschnitt MCP — auch hier bringt jedes Paket seine Agenten-Oberfläche mit

Es gelten dieselben Regeln wie in [hardeningAndParity.md](hardeningAndParity.md) §3a und [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md): Berechtigungsstufe am Werkzeug deklariert, Eintrag in der Katalog-Momentaufnahme, Schreibwerkzeuge in der Wirkungskontrolle, `CorrelationId` durchgezogen. Sollzustand:

| Paket | Lesend | Schreibend |
|---|---|---|
| AR-1 | `list_incidents`, `get_incident` (Zeitachse, Evidenz-Metadaten) | `acknowledge_incident` |
| AR-3 | `get_workload_state` (inkl. `stopped-by-policy`) | — |
| AR-4 | `get_blocklist` | `set_blocklist` (write, TTL-Pflicht, Allowlist unantastbar) |
| AR-5 | `list_response_policies` | abgestufte Reaktionen bis Stufe 4; **Stufe 5 nie** |
| AR-6 | `get_incident_chain` (Auslöser → Aktion → Ergebnis) | **kein `unlock`** |

**Die Auslassungen sind der wichtigere Teil dieser Tabelle.** Ein Agent, der eine Schutzabschaltung selbst aufheben kann, macht die Schutzabschaltung wertlos; und eine Sperre ohne Ablauf, die ein Agent setzen darf, kann die eigene Verwaltung aussperren. Beide Grenzen gehören in die Guardrail-Regeln, nicht in eine Konvention.

---

## 4. Arbeitspakete

### AR-1 — Ingest-API + Incident-Objekt (Fundament)

**Lücke:** Kein Endpunkt zum Melden, kein Objekt zum Führen. Ohne AR-1 ist jedes andere Paket unanschließbar — deshalb steht es zuerst.

**Implementierung:**
- Neue Entität `SecurityIncidentEntity` in `Whiskers.Data/Entities/`: `IncidentId` (Anzeigeform `WSK-YYYY-MMDD-NNNN`), `DedupeKey`, `ServerId`, `Source` (`dbguard`, `logmonitor`, `manual`), `Kind`, `Severity`, `State` (`open/acknowledged/contained/resolved`), `FirstSeen`/`LastSeen`, `EventCount`, `Classification`, `CorrelationId`, `ClosedBy`/`ClosedAt`. Dazu `SecurityIncidentEventEntity` (Zeitachse) und `SecurityIncidentEvidenceEntity` (Anhang-Metadaten; Blobs als Sidecar-Dateien unter `/app/data/incidents/<id>/`, **nicht** in die DB — dasselbe Muster wie die F3-Manifeste).
- **Additive Migration in BEIDEN Assemblies** (`Whiskers.Migrations.Sqlite` + `Whiskers.Migrations.Postgres`, ADR-0004). Keine bestehende Spalte anfassen. `SqliteToPostgresMigrator` um die neuen Tabellen erweitern, `DatabaseInitializer.LegacyHealSql` ebenfalls.
- `POST /api/monitoring/events` in `WhiskersPipelineExtensions`: Schema-validiertes JSON, **Maschinen-Token pro Sidecar** (neuer `AgentTokenStore`, Hash at rest, Rotation, Bindung an `serverId` — der Webhook-HMAC ist an eine Deploy-Aktion gebunden und wird ausdrücklich **nicht** wiederverwendet), Idempotenz über `DedupeKey` (`upsert`, `EventCount++`, `LastSeen`), Antwort `202 { incidentId, state }`. Body-Größenlimit, Rate-Limit pro Token, `AllowAnonymous` nur bezogen auf Cookie-Auth.
- `POST /api/monitoring/incidents/{id}/evidence`: Multipart, Größen- und Anzahllimit, MIME-Whitelist, Content-Hash im Manifest.
- Bestehende Alarme mit-verdrahten: `AlertHistoryEntity` bleibt unangetastet (Rückwärtskompatibilität), aber `CompositeNotificationService` bekommt eine optionale Incident-Referenz, damit Feed und Incident zusammenfallen.
- UI: `/incidents` (Liste, Filter nach State/Server/Kind) und `/incidents/{id}` (Zeitachse, Evidenz, Aktionen). Modul-fähig schneiden (`IModuleRegistry`, wie in RoadToSAP §3), i18n von Anfang an (`Page_Element_Purpose`).

**Abhängigkeiten:** keine. Kann sofort starten.

---

### AR-2 — Detektion: von „Zeile matcht" zu „Muster über Zeit"

**Lücke:** Einzelzeilen-Match mit `break` + Cooldown. Keine Rate, kein Fenster, keine Gruppierung, keine Negativbedingung.

**Implementierung:**
- `LogAlertRuleEntity` **additiv** erweitern (Default-Werte müssen bestehendes Verhalten exakt erhalten): `WindowSeconds` (0 = heutiges Verhalten), `Threshold`, `GroupByCaptureGroup`, `DistinctThreshold`, `SuppressIfCaptureSeen`, `EscalationLevels`.
- Zweiter Regeltyp im Scanner: statt `break` beim ersten Treffer **alle** Treffer des Fensters zählen, benannte Regex-Gruppen (`(?<host>…)`, `(?<user>…)`) extrahieren und **distinct** zählen. „16 Fehler von 4 Hosts" gegen „381 von 45 Hosts, 18 Usernamen" ist der ganze Unterschied zwischen Rauschen und Angriff.
- `SuppressIfCaptureSeen` ist die entscheidende Negativbedingung: `0 successful auths`. Ohne sie ist jeder Montagmorgen ein Incident.
- Eskalationsstufen statt Cooldown-Stille: `warn` → `crit` → `sustained` aktualisieren denselben Incident (Dedupe-Key), statt neue Alarme zu erzeugen oder zu schweigen.
- **Streaming für als kritisch markierte Container:** `docker logs --follow` über eine stehende Verbindung (Muster von F6 `DockerEventMonitor` mitnutzen), Poll bleibt Fallback. Ohne das bleiben 200-Zeilen-Cap und 60-s-Kadenz die harte Obergrenze.
- Selbst-Loop-Schutz erweitern: `SelfContainerNames` deckt nur den eigenen Container ab. Ein Sidecar, der in fremdes stdout schreibt, umgeht ihn — Ingest-Events (AR-1) dürfen deshalb **nie** über den Log-Pfad zurücklaufen; Ingest-erzeugte Incidents werden im Scanner explizit ausgeschlossen.
- Metrik-basierte Regeln als zweiter Signalpfad: `IPrometheusMetricsSource` liest bereits — ein Schwellwert auf eine Query (VictoriaMetrics) ist ein kleiner Zusatz und deckt Fälle ab, die im Log nie stehen.

**Abhängigkeiten:** AR-1 (Incident als Ziel der Eskalation). Streaming-Teil profitiert von F6.

---

### AR-3 — Zustand `stopped-by-policy` (Konfliktvermeidung)

**Lücke:** Der schwerwiegendste Befund aus §2.2 — Monitoring und Schutzmaßnahme bekämpfen einander.

**Implementierung:**
- Neuer Container-Zustand `ExpectedState` (`running` | `stopped-by-policy` | `maintenance`) in einem `IWorkloadStateService`, persistiert, gesetzt durch Ingest (AR-1) oder manuell.
- `ContainerHealthMonitor` und `ServerReachabilityTracker` unterdrücken Down-Alarme für Workloads mit `stopped-by-policy` — melden stattdessen **einmal** den Übergang und danach nur noch den Fortbestand am Incident.
- `TaskExecutor` (Restart-Tasks), Auto-Update und der Agent-Pfad verweigern Start/Restart auf gesperrten Workloads **fail-closed** mit klarer Begründung. Als Guardrail-Regel implementieren (`workload-locked`), nicht als verstreute `if`s — dann greift sie automatisch für jedes künftige Tool.
- UI: gesperrte Workloads sichtbar anders darstellen als Ausfälle (Schloss-Zustand + Verweis auf den Incident), nicht rot-als-Fehler.
- **Deadman-Switch:** bleibt der Sidecar-Heartbeat (`state=LOCKED`, alle 60 s) aus, ist *das* der Alarm. Fehlender Heartbeat ist ein eigener Incident-Kind, keine Stille.

**Abhängigkeiten:** AR-1.

---

### AR-4 — Blocklist-Dienst mit TTL

**Lücke:** ufw-Einzelregeln, Löschen nach Regelnummer, kein IPv6-Präfix-Handling, kein Ablauf.

**Implementierung:**
- Neuer `IBlocklistService` **neben** `IFirewallService` (letzterer bleibt für manuelle Port-Regeln unverändert): deklarativ (`SetAsync(serverId, entries, ttl)`), idempotent, IPv6-fähig, mit Präfix-Aggregation (`2001:db8:c57:a4::/64` statt 40 Einzeladressen).
- Backend: nftables-Set bzw. ipset mit nativem `timeout` — der Kernel übernimmt den Ablauf, kein Aufräum-Job, kein Regelnummern-Geschiebe. Fallback auf ufw nur, wo kein Set verfügbar ist.
- Schutzgrenzen fail-closed: eine Allowlist (Mesh-/Tailnet-Bereiche, Whiskers selbst, konfigurierte Admin-Netze) kann **nie** gesperrt werden. Sonst sperrt sich die Automatik selbst aus — vergleiche das Tailnet-Datenebenen-Problem beim Deploy.
- Als MCP-Tool auf Level `write` exponieren, damit AR-6 es unter Guardrails nutzen kann. Jede Sperre und jede Entsperrung ins Audit-Log mit `CorrelationId`.

**Abhängigkeiten:** keine harten; sinnvoll nach AR-1, damit Sperren am Incident hängen.

---

### AR-5 — Abgestufte Reaktionen (der Shutdown ist die letzte Stufe)

**Lücke:** Heute gibt es nur „nichts tun" oder „Container stoppen".

**Implementierung — Stufen, aufsteigend:**
1. **Ratelimit** am Reverse-Proxy für den betroffenen Pfad (`NginxService` kann bereits Konfiguration schreiben).
2. **Tarpit** auf dem Login-Endpunkt — verzögern statt sperren, hält Angreifer-Threads fest.
3. **Read-Only** für den Dienst, wo das Datenmodell es hergibt.
4. **Netzwerk-Isolation** des Containers (Netz trennen) — Dienst lebt, Angreifer kommt nicht mehr ran, Zustand bleibt für die Forensik erhalten. Meist die bessere Wahl als Stufe 5.
5. **Protective Shutdown** + Lock (AR-3).

Jede Stufe ist eine benannte Policy mit explizitem Opt-in, sichtbarer Konsequenz in der UI und Audit-Eintrag.

> ⚠️ **Ehrlichkeit gegenüber Nutzern (Pflichttext in der UI, nicht nur hier):** Der Protective Shutdown ist ein bewusstes **Self-DoS** — Vertraulichkeit gegen Verfügbarkeit. Im Referenzszenario hatte der Angreifer `successful_auths=0`, hat also nichts erreicht, und die Vereins-Cloud stand trotzdem. Das darf **niemals** ein Default sein, und die Policy-Beschreibung muss die Konsequenz beim Namen nennen.

**Abhängigkeiten:** AR-3 (sonst kämpft das Monitoring dagegen), AR-4.

---

### AR-6 — Governance: Freigabe, Entsperrung, Beweiskette

**Lücke:** Es gibt Approvals für Agenten-Aktionen, aber keinen Entsperr-Vorgang und keine belastbare Beweiskette für einen Sicherheitsvorfall.

**Implementierung:**
- **Unlock als Governance-Akt:** `dbguard unlock --confirm` bekommt eine Entsprechung in Whiskers — Zwei-Personen-Freigabe über den bestehenden `ApprovalCoordinator`, im Audit-Log, mit Pflichtbegründung. **Nie** durch den Agenten allein, auch nicht auf Level `admin`.
- `AiTriggerDispatcher` auf Incident-**Zustandsübergänge** hören lassen, nicht auf jedes Einzelereignis — sonst feuert er im Referenzfall 381-mal. Neuer Event-Typ `incident_state_changed`; die bestehende Rekursionssperre (`agent_action*`) entsprechend erweitern.
- Beweiskette: `CorrelationId` (WP-05) durchgängig von Ingest über Incident, Agentenlauf, Approval bis zur Aktion. Export eines Incidents als signiertes Paket (Zeitachse + Evidenz + Audit-Auszug) — ein Incident, dessen Beweise nur im Container liegen, der gerade neu gebaut wird, ist keiner.
- Retention getrennt regeln: Metrik-Prune (90 Tage) darf Incident-Evidenz **nicht** wegräumen.

**Abhängigkeiten:** AR-1, AR-3.

---

### AR-7 — `whiskers-guard` als eigenes Artefakt

**Lücke:** Die Datenebene existiert nur als fremder Sidecar im Referenzszenario.

**Implementierung:**
- Eigenes, schlankes Image (eigenes Repo oder `deploy/guard/`): liest lokal (Log-Stream/Socket), zählt in Fenstern, wendet lokale Policy an, handelt lokal, meldet über AR-1 nach oben, sendet Heartbeat.
- Konfiguration deklarativ (YAML), Policies mitgeliefert für die häufigen Fälle: MySQL/Postgres-Auth-Brute-Force, HTTP-Login-Stuffing, SSH.
- **Vom Dienst getrennte Rechte:** der Guard braucht Lese-Zugriff auf Logs und genau eine privilegierte Aktion — nicht mehr. Kein Docker-Socket, wo es vermeidbar ist.
- Onboarding-Integration: `OnboardingService` stellt den Guard beim Aufnehmen eines Servers mit auf, Token wird dabei erzeugt und einmalig angezeigt (Muster wie Webhook-Secret).

**Abhängigkeiten:** AR-1 (Protokoll), AR-4/AR-5 (was er lokal tun darf).

---

## 5. Reihenfolge

| Welle | Pakete | Ergebnis |
|---|---|---|
| **1 — Anschlussfähigkeit** | AR-1, AR-3 | Whiskers kann den Referenz-Log **ab `17:29:06` echt produzieren**: Incident annehmen, führen, Evidenz halten, gesperrten Container korrekt darstellen statt dagegen zu arbeiten. Den Detektionsteil davor liefert bis dahin der Sidecar. |
| **2 — Eigene Augen** | AR-2, AR-4 | Whiskers erkennt Muster selbst (auch ohne Sidecar) und kann gezielt sperren statt nur zu stoppen. |
| **3 — Produkt** | AR-5, AR-6, AR-7 | Abgestufte Reaktion, Zwei-Personen-Entsperrung, mitgeliefertes Guard-Artefakt. Ab hier trägt die Aussage „Whiskers schützt". |

AR-1 und AR-3 zusammen sind der kleinste sinnvolle Schnitt. Alles davor ist Vorarbeit ohne sichtbaren Nutzen, alles danach baut darauf auf.

---

## 6. Bewusst NICHT gebaut

- **Kein SIEM.** Keine Volltext-Log-Aggregation über die Flotte, keine Query-Sprache, kein Langzeit-Log-Storage. Whiskers führt Incidents, es archiviert keine Logs. Wer das braucht, nimmt Loki/OpenSearch daneben.
- **Kein IDS/IPS am Netz.** Keine Paketinspektion, keine Signaturdatenbank.
- **Kein Threat-Intel-Feed.** Keine externen Reputationslisten in v1 — das ist eine Datenquelle mit eigener Pflege- und Datenschutzfrage.
- **Keine automatische Entsperrung nach Protective Shutdown.** Bewusst: Freigabe bleibt Operator-Entscheidung (AR-6).
- **Kein eigener fail2ban-Ersatz.** fail2ban bleibt am Dienst, wo es hingehört; Whiskers übernimmt, wenn es *nicht mehr konvergiert* — genau der Moment, den das Referenzszenario in `17:28:46` markiert.

---

## 7. Offene Entscheidungen (Nutzer-Entscheidung, nicht vorwegnehmen)

1. **`whiskers-guard` — eigenes Repo oder mit im Produkt-Repo?** Eigenes Repo entkoppelt die Release-Zyklen, kostet aber eine zweite Pipeline (Multi-Arch, Trivy-Gate, cosign, SBOM — alles wie in `release.yml`).
2. **Ist AR-5 Stufe 5 (Protective Shutdown) überhaupt Teil des Produkts** oder bleibt es beim Sidecar? Argument dafür: ohne es ist die Kette unvollständig. Argument dagegen: eine Verfügbarkeits-kostende Aktion in einem Verwaltungswerkzeug ist ein Support-Risiko.
3. **Maschinen-Token-Modell** — pro Sidecar, pro Server oder pro Flotte? Empfehlung: pro Sidecar, an `serverId` gebunden, rotierbar.
4. **Zielversion:** AR-1/AR-3 sind additiv und 1.0-verträglich; AR-5/AR-7 sind eher 1.1.

---

## 8. Definition of Done (Welle 1)

- [ ] `SecurityIncidentEntity` + Zeitachse + Evidenz-Metadaten, **additive Migration in beiden Migrations-Assemblies**, `SqliteToPostgresMigrator` und `LegacyHealSql` erweitert
- [ ] `POST /api/monitoring/events` mit Maschinen-Token, Schema-Validierung, Idempotenz über Dedupe-Key, `202 { incidentId, state }`; Rate- und Größenlimit
- [ ] Evidenz-Upload mit Hash-Manifest unter `/app/data/incidents/<id>/`; von der Metrik-Retention ausgenommen
- [ ] `/incidents` + `/incidents/{id}` als Modul, i18n EN/DE vollständig
- [ ] `ExpectedState = stopped-by-policy` unterdrückt Down-Alarme, blockiert Start/Restart fail-closed über eine Guardrail-Regel (nicht verstreute `if`s), UI zeigt Sperre statt Ausfall
- [ ] Deadman-Switch: ausbleibender Heartbeat erzeugt einen eigenen Incident
- [ ] Tests: Ingest-Idempotenz, Token-Ablehnung, Zustandsautomat, Suppression-Verhalten des Health-Monitors, Migrations-Baseline (beide Provider)
- [ ] DI-Boot-Gate in beiden Auth-Modi grün (Projektregel — Build + Unit-Tests fangen DI-Graph-Regressionen nicht)
- [ ] Datensicherheit belegt: Migration gegen eine Kopie einer echten `metrics.db` **und** gegen Postgres gefahren, vorher Backup, Ergebnis dokumentiert

---

## 9. Querverweise

- [../ARCHITECTURE.md](../ARCHITECTURE.md) — die drei Ebenen, Mesh + mTLS; `whiskers-guard` ist eine vierte Komponente in dieser Topologie
- [../adr/0004-postgres-provider-support.md](../adr/0004-postgres-provider-support.md) — warum jede Migration in **zwei** Assemblies muss
- [../product/POSITIONING.md](../product/POSITIONING.md) — „Whiskers meldet" → „Whiskers schützt"
