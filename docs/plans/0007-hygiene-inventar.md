# Plan-0007: Hygiene-Inventar (SP-7)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0007](../prd/0007-hygiene-inventar.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Zwei Container haben den Vorfall ausgelöst: `socket-proxy` und `ghostunnel` — die beiden, über die Whiskers seinen Docker-Zugriff führt und die deshalb jede seiner Anfragen protokollieren. Ohne Rotationslimit wuchsen sie in zwei Wochen auf zusammen 822 MB.

Dieser Plan ist unabhängig von allen anderen und billig. Er nimmt den **Auslöser** weg. Er behebt **nicht** die Ursache — das tun Plan-0001 und Plan-0002. Diese Unterscheidung muss auch in den erzeugten Meldungen erkennbar sein, sonst wird Symptombehandlung für Heilung gehalten.

## Ziele

- Whiskers scannt nicht mehr die Logs, die sein eigener Verkehr erzeugt.
- Container ohne Rotationslimit sind bekannt, bevor sie zum Problem werden.
- Der Behebungsweg wird geliefert, nicht ausgeführt.

## Arbeitspakete

### WP1: Zugriffspfad-Container erkennen

**Zweck:** Die Rückkopplung beenden.
**Schätzung:** M (1,5 Tage) — die Erkennung ist der schwierige Teil, nicht der Ausschluss.

1. **WP1.1:** Je Server den konfigurierten Zugriffsweg auswerten (`ServerConfig`: Socket, TCP-Ziel, Tunnel-Endpunkt).
2. **WP1.2:** Zieladresse und Port gegen die Port-Bindings der Container dieses Servers abgleichen; Treffer = Zugriffspfad-Container.
3. **WP1.3:** `SERVERWATCH_SELF_CONTAINERS` bleibt als manuelle Übersteuerung erhalten und hat Vorrang.
4. **WP1.4:** Diese Container aus dem Log-Scan nehmen — **nicht** aus Health, Metriken oder CVE. Sie sollen weiter überwacht werden, nur ihr Loginhalt ist wertlos.

**Ergebnis:** Der Log-Monitor frisst nicht mehr sein eigenes Zugriffsprotokoll.

**Abnahme:** Eine Regel auf ein Muster, das nur im Proxy-Log vorkommt, liefert nach der Änderung keine Treffer mehr.

### WP2: Ausschlüsse sichtbar machen

**Zweck:** Verhindern, dass der Ausschluss selbst zum blinden Fleck wird.
**Schätzung:** S (0,5 Tage).

1. **WP2.1:** Liste der vom Log-Scan ausgenommenen Container in der Serveransicht, mit Begründung („Zugriffspfad", „manuell ausgeschlossen").
2. **WP2.2:** Kennzahl über die Zahl der Ausschlüsse — wächst sie ohne Konfigurationsänderung, ist die Erkennung zu gierig.

**Abnahme:** Ein Container, der zufällig `socket-proxy` heißt, aber nicht im Zugriffspfad liegt, wird weiter gescannt und erscheint nicht in der Liste.

### WP3: Log-Inventar

**Zweck:** Die Zeitbombe melden, bevor sie zündet.
**Schätzung:** M (1,5 Tage).

1. **WP3.1:** Tägliche Prüfung je Container: `HostConfig.LogConfig` und Größe der Datei hinter `LogPath`. Läuft unter dem Budget aus Plan-0001, ein Aufruf je Container und Tag.
2. **WP3.2:** Wo die Größe ohne erhöhte Rechte nicht lesbar ist: `unbekannt` ausweisen, nicht schätzen.
3. **WP3.3:** Wachstumsrate aus zwei aufeinanderfolgenden Prüfungen, Angabe in MB/Tag.
4. **WP3.4:** Bewertung **relativ zum freien Plattenplatz**, nicht absolut — 100 MB sind auf verschiedenen Servern verschieden bedeutsam.

**Abnahme:** Ein künstlich auf 150 MB gebrachtes Log ohne Rotationslimit erscheint beim nächsten Lauf mit korrekter Größe und plausibler Wachstumsrate (Abweichung < 20 % gegen `du -sh`).

### WP4: Hinweis und Behebungsbefehl

**Zweck:** Aus einem Befund eine Handlung machen — ohne sie auszuführen.
**Schätzung:** S (1 Tag).

1. **WP4.1:** Inventar-Hinweis für „kein Rotationslimit" (ohne Meldung).
2. **WP4.2:** Meldung für „kein Rotationslimit **und** Größe über Schwelle", mit Server, Container, Größe, Wachstumsrate.
3. **WP4.3:** Fertiger, wörtlich lauffähiger Befehl zum Kopieren, mit den konkreten Werten des Containers und dem Hinweis, dass der Container dabei neu erzeugt wird.
4. **WP4.4:** Formulierung so wählen, dass klar bleibt: Das behebt den Auslöser, nicht die Ursache — Verweis auf den Stand von SP-1/SP-2.
5. **WP4.5:** Hinweis auf einen fehlenden Rotations-Default in `/etc/docker/daemon.json`, wo ermittelbar.

**Abnahme:** Der ausgegebene Befehl läuft auf einem Testserver ohne Nacharbeit.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_log_hygiene_report` — Befunde und vorbereiteter Behebungsbefehl als Text. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Kein schreibendes Werkzeug vorhanden — Gegenprobe im Katalog. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2
WP3 ──> WP4
```

- Beide Stränge sind unabhängig voneinander und von allen anderen Plänen — dieser Plan kann sofort und parallel laufen.
- **Empfehlung:** WP1/WP2 zuerst, weil sie die aktive Rückkopplung beenden.
- Nutzt das Budget aus Plan-0001, sobald vorhanden; funktioniert auch ohne.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Rückkopplung beendet | Zeilenrate im Proxy-Log vor/nach | deutlich gesunken |
| Zahl der Ausschlüsse | Kennzahl WP2.2 | genau die Zugriffspfad-Container |
| Inventar-Abdeckung | Ansicht WP3 | wenige `unbekannt` |
| Vorlaufzeit | Zeit zwischen erstem Hinweis und kritischer Größe | > 3 Tage |
| Wachstumsrate | WP3.3 gegen `du -sh` | Abweichung < 20 % |
| Befehlsqualität | Stichprobe auf Testserver | läuft ohne Nacharbeit |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Fehlausschluss | Container verschwindet lautlos aus der Log-Überwachung | WP2.1 dauerhaft sichtbar; Erkennung über Zugriffspfad, nicht über Namen |
| Namensbasierte Fehltreffer | fremder Container mit gleichem Namen wird blind | WP1.2 als Wahrheit, WP1.3 nur als Übersteuerung |
| Hinweis wird nie gelesen | Bombe zündet trotzdem | Eskalation zur Meldung ab 25 % des freien Plattenplatzes |
| Symptombehandlung gilt als Heilung | SP-1/SP-2 werden verschoben | WP4.4 — die Meldung sagt es selbst |
| Inventarprüfung erzeugt Last | derselbe Fehler eine Ebene tiefer | ein Aufruf je Container und Tag, unter dem Budget |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 + WP2 | Musterregel liefert keine Proxy-Treffer mehr; Namensgleicher Container bleibt im Scan |
| M2 | WP3 | 150-MB-Testlog korrekt erfasst, Wachstumsrate plausibel |
| M3 | WP4 | Befehl läuft wörtlich auf einem Testserver |
| M4 | Feldlauf | 14 Tage auf der echten Flotte; Proxy-Logzeilenrate protokolliert |

## Rückweg

Die Erkennung aus WP1 ist über `SERVERWATCH_SELF_CONTAINERS` vollständig übersteuerbar. Erweist sie sich als zu gierig, wird sie deaktiviert und die manuelle Liste genutzt — der Sichtbarkeitsteil aus WP2 bleibt in jedem Fall.

## Definition of Done

- [ ] WP1–WP4 umgesetzt
- [ ] Proxy-Log-Muster erzeugt keine Alarmtreffer mehr
- [ ] Namensgleicher Container außerhalb des Zugriffspfads bleibt im Scan
- [ ] Ausschlussliste dauerhaft sichtbar, mit Begründung
- [ ] 150-MB-Gegenprobe erzeugt genau einen Hinweis mit korrekter Größe
- [ ] Behebungsbefehl auf einem Testserver wörtlich lauffähig
- [ ] Meldungstext benennt ausdrücklich, dass dies den Auslöser behebt und nicht die Ursache
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
