# PRD-0009: Externe Checks & Status-Seite (GAP-2)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte, deren Nutzer (Empfänger der Status-Seite)
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — GAP-2
- **Ersetzt:** —

## Problem / Motivation

Whiskers beobachtet ausschließlich von innen: Container-Zustände, Metriken vom Host, Logzeilen. Ob ein Dienst **von außen** erreichbar ist, weiß es nicht. Ein Container kann laufen, gesund gemeldet werden und trotzdem für Nutzer nicht erreichbar sein — falsches Zertifikat, kaputte Reverse-Proxy-Regel, DNS-Eintrag abgelaufen, Firewall-Regel verstellt.

Der Vergleichspunkt ist deutlich: Uptime Kuma ist das Standardwerkzeug für genau diese Außensicht — HTTP/TCP/Ping/DNS/Keyword-Prüfungen, über 90 Benachrichtigungskanäle, Status-Seiten. Whiskers hat neun Kanäle, keine synthetischen Prüfungen und keine Status-Seite.

Der übliche Rat lautet, beides nebeneinander zu betreiben: Außensicht für Symptome, Innensicht für Ursachen. Genau darin liegt aber die Chance — die Kombination in **einem** Werkzeug erspart den Abgleich zwischen zwei Systemen, der heute von Hand passiert: „Dienst antwortet nicht" (außen) und „Container läuft, aber die Datenbank ist weg" (innen) sind zwei Hälften einer Meldung.

## Ziele

- Whiskers erkennt, dass ein Dienst von außen nicht erreichbar ist — unabhängig davon, was der Container meldet.
- Außen- und Innensicht erscheinen in **einer** Meldung mit gemeinsamer Ursachenaussage.
- Betreiber können ihren Nutzern eine Status-Seite zeigen, ohne ein zweites Werkzeug zu betreiben.

## Non-Goals

- **Keine** Uptime-Kuma-Parität bei der Kanalzahl. Neun gute Kanäle plus ein generischer Webhook decken den Bedarf; 90 Integrationen sind kein Ziel.
- **Keine** verteilten Prüfpunkte aus mehreren Weltregionen.
- **Keine** SLA-Berechnung, Vertragsauswertung oder Abrechnung.
- **Keine** öffentliche Multi-Tenant-Status-Seite mit Mandantentrennung — eine Seite je Whiskers-Installation.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Erfährt von einem Ausfall durch Nutzer, nicht durch das Monitoring, weil innen alles grün ist.

### Nutzer der betriebenen Dienste

- Kontext: Vereinsmitglieder, Kunden.
- Pain Point: Weiß bei einer Störung nicht, ob es an ihnen liegt — und fragt beim Betreiber nach.

### Betreiber mit Uptime Kuma daneben

- Pain Point: Pflegt zwei Alarmwege und muss bei jedem Vorfall zwei Oberflächen abgleichen.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Prüfarten: HTTP(S) mit Statuscode- und Inhaltsprüfung, TCP-Port, ICMP-Ping, DNS-Auflösung. | Must |
| FR-02 | Je Prüfung: Intervall, Zeitüberschreitung, Anzahl Fehlversuche bis Alarm, erwarteter Wert. | Must |
| FR-03 | TLS-Zertifikatsprüfung mit Vorwarnung vor Ablauf (Default 21 Tage). | Must |
| FR-04 | Eine Prüfung ist optional einem Container/Workload zugeordnet; die Meldung enthält dann Außenbefund **und** Innenzustand. | Must |
| FR-05 | Prüfungen laufen aus dem Whiskers-Prozess heraus und respektieren dessen Lastbudget (SP-1). | Must |
| FR-06 | Öffentliche Status-Seite: auswählbare Prüfungen, Verfügbarkeit über 7/30/90 Tage, aktuelle Störungen, ohne Anmeldung erreichbar. | Must |
| FR-07 | Die Status-Seite ist einzeln ein- und abschaltbar und zeigt nur ausdrücklich freigegebene Prüfungen — nie automatisch alle. | Must |
| FR-08 | Wartungsfenster: geplante Zeiträume unterdrücken Alarme und erscheinen auf der Status-Seite als angekündigt. | Should |
| FR-09 | Die Ergebnisse der Prüfungen sind Zeitreihen und über die vorhandene Retention auswertbar. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `list_checks` und `get_check_status` (read), `run_check_now` (write). Damit kann der Agent bei einer Störungsmeldung Außen- und Innensicht selbst zusammenführen. | Must |

## Nicht-Funktionale Anforderungen

- **Die Status-Seite muss den Ausfall überleben.** Eine Seite, die mitfällt, wenn der überwachte Dienst fällt, ist wertlos — Abhängigkeiten von der überwachten Umgebung sind zu vermeiden und offen zu dokumentieren.
- **Keine Datenpreisgabe:** Die öffentliche Seite verrät keine internen Adressen, Container- oder Servernamen, sofern nicht ausdrücklich freigegeben.
- **Prüfintervall-Untergrenze:** 30 Sekunden, damit die Prüfungen nicht selbst zur Last werden (Lehre aus dem Vorfall).

## User Stories

- **US-01:** Als Betreiber möchte ich erfahren, dass meine Cloud von außen 502 liefert, auch wenn alle Container „gesund" melden.
- **US-02:** Als Betreiber möchte ich meinen Nutzern eine Seite geben, auf der sie den Störungsstand selbst sehen.
- **US-03:** Als Betreiber möchte ich vor einem ablaufenden Zertifikat gewarnt werden, nicht danach.

### Flow für US-01

```
Given der Reverse-Proxy liefert 502, der App-Container meldet "healthy"
When zwei aufeinanderfolgende HTTP-Prüfungen fehlschlagen
Then erscheint eine Meldung: "burgcloud.example: HTTP 502 seit 2 min (außen),
     Container burgcloud_app läuft, Datenbank nicht erreichbar (innen)"
```

## Akzeptanzkriterien

- FR-01 bis FR-07 umgesetzt.
- Gegenprobe: Ein absichtlich gestoppter Reverse-Proxy erzeugt innerhalb von zwei Prüfintervallen eine Meldung, während der Container-Zustand unverändert „läuft" ist. Genau dieser Fall ist der Daseinsgrund des Pakets.
- Zertifikatswarnung löst 21 Tage vor Ablauf aus — geprüft mit einem kurzlebigen Testzertifikat.
- Die Status-Seite bleibt erreichbar, während der geprüfte Dienst ausgefallen ist.
- Die Status-Seite zeigt ausschließlich freigegebene Prüfungen — mit einem Test, der eine nicht freigegebene Prüfung anlegt und ihre Abwesenheit auf der Seite belegt.
- MCP: Der Agent stellt bei einem simulierten Ausfall Außenbefund und Container-Zustand ohne menschliche Zwischenschritte gegenüber.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Erkennungszeit außen | manuelle Störung, Zeit bis Meldung | ≤ 2 Intervalle | länger ⇒ Fehlversuchsschwelle zu hoch |
| Fehlalarme durch Netzsprünge | Meldungen je Woche ohne echten Ausfall | ≤ 1 | mehr ⇒ Prüfungen werden ignoriert werden |
| Eigenlast der Prüfungen | `whiskers_self_`-Aufrufrate (SP-3) | konstant, planbar | wächst mit der Prüfungszahl überproportional ⇒ kein Budget |
| Status-Seite im Ausfall | Ausfall des geprüften Dienstes herbeiführen | Seite lädt | Seite fällt mit ⇒ Kernanforderung verfehlt |
| Datenpreisgabe | Seitenquelltext prüfen | nur freigegebene Namen | interne Hostnamen sichtbar ⇒ FR-07 verletzt |
| Zertifikatsvorlauf | Testzertifikat mit 20 Tagen Restlaufzeit | Warnung erscheint | keine Warnung ⇒ Prüfung greift auf die falsche Kette |

## Woran ich sehe, dass es bricht

1. **Die Status-Seite fällt mit dem Dienst.** Der klassische Fehler: Whiskers läuft auf demselben Host, hinter demselben Reverse-Proxy wie die überwachten Dienste. Fällt der Proxy, ist die Statusseite ebenfalls weg — und zwar genau dann, wenn sie gebraucht wird. **Gegenprobe, die verpflichtend ist:** den Reverse-Proxy stoppen und prüfen, ob die Seite noch antwortet. Fällt sie mit, muss das in der Oberfläche als Einschränkung dokumentiert werden, statt Sicherheit vorzutäuschen.
2. **Prüfungen, die die eigene Störung nicht sehen.** Läuft eine Prüfung aus demselben Netz wie der Dienst, prüft sie den kurzen Weg und nicht den, den Nutzer nehmen. Sie bleibt grün, während draußen nichts geht. **Messstelle:** die Zieladresse einer Prüfung gegen den öffentlichen Namen abgleichen; interne IPs oder `localhost` als Ziel sind ein Konfigurationsfehler und gehören markiert.
3. **Flatternde Prüfungen erziehen zum Wegsehen.** Ein Check, der wöchentlich zweimal falsch meldet, wird nach einem Monat ignoriert. **Messstelle:** Verhältnis Meldungen zu bestätigten Ausfällen je Prüfung; unter 50 % ist die Prüfung schädlich und gehört entschärft oder entfernt.
4. **Die Seite verrät die Infrastruktur.** Eine öffentliche Seite, die interne Hostnamen, Serverbezeichner oder Fehlermeldungen im Klartext zeigt, ist eine Aufklärungshilfe. **Gegenprobe:** Quelltext und API-Antworten der Seite auf interne Bezeichner prüfen — nicht nur die sichtbare Darstellung.
5. **Prüfungen werden selbst zur Last.** 200 Prüfungen im 30-Sekunden-Takt sind 400 Verbindungen pro Minute aus einem Prozess, der bereits eine Flotte abfragt. Das ist der Vorfall vom 26.08. in neuer Kleidung. **Messstelle:** Eigenlast in SP-3, mit einer harten Obergrenze für gleichzeitig laufende Prüfungen.

## Do's

- **Die Störung nachstellen**, bevor der Check als fertig gilt — Dienst wirklich stoppen, nicht simulieren.
- **Außen- und Innenbefund in einer Meldung** zusammenführen (FR-04). Das ist der Mehrwert gegenüber zwei getrennten Werkzeugen.
- **Freigabe je Prüfung** für die öffentliche Seite, niemals „alle anzeigen".
- **Prüfziele nach öffentlichem Namen** konfigurieren, nicht nach interner Adresse.

## Don'ts

- **Nicht** die Kanalzahl von Uptime Kuma nachbauen wollen. Ein generischer Webhook ersetzt achtzig Integrationen.
- **Nicht** Prüfintervalle unter 30 Sekunden anbieten.
- **Nicht** die Status-Seite hinter dieselbe Authentifizierung hängen wie die Anwendung — sie ist für Menschen ohne Zugang gedacht.
- **Nicht** Wartungsfenster als „Alarm unterdrücken" bauen, ohne sie auf der Status-Seite anzukündigen. Sonst ist es Verschweigen.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 (Budget, FR-05). Ohne Budget wird dieses Paket zur nächsten Lastquelle.
- **Verwandt:** SP-4 (gemeinsame Schwellen-/Dauer-Logik nutzen, nicht zweimal bauen).

## Offene Fragen

- **F-01:** Soll die Status-Seite optional aus einem zweiten, kleinen Prozess bedient werden können, damit sie den Ausfall sicher überlebt? Aufwand gegen Nutzen abwägen — Vorschlag: v1 ohne, mit klarer Dokumentation der Einschränkung.
- **F-02:** Eigene Domain/Port für die Status-Seite oder Unterpfad? Vorschlag: Unterpfad in v1, eigener Port als Option.
