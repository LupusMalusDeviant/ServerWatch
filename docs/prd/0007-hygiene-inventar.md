# PRD-0007: Hygiene-Inventar (SP-7)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber der verwalteten Flotte
- **Auslöser:** [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md), Vorschläge 5 und 6 sowie Signal 4
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — SP-7
- **Ersetzt:** —

## Problem / Motivation

Zwei Container haben den Vorfall ausgelöst: `socket-proxy` und `ghostunnel` — ausgerechnet die beiden, über die Whiskers seinen Docker-Zugriff führt. Beide protokollieren jede API-Anfrage, also auch jede Anfrage des Log-Monitors: rund 110 Zeilen pro Minute, gut 80 MB pro Tag. Ohne Rotationslimit wuchsen sie in zwei Wochen auf zusammen 822 MB.

Damit sind zwei Defekte benannt:

**Erstens** eine Rückkopplung: Der Log-Monitor erzeugt selbst die Logzeilen, an denen er sich anschließend festfrisst. Je mehr er sich verschluckt, desto mehr Zeilen entstehen. Der vorhandene Selbstausschluss (`SelfContainerNames`) kennt nur den eigenen Container — die Container, die den eigenen *Verkehr* protokollieren, sind aber ebenso „selbst".

**Zweitens** eine unbemerkte Zeitbombe: Ein Container ohne Rotationslimit wächst unbegrenzt. Whiskers sieht `HostConfig.LogConfig` jedes Containers und kennt über `LogPath` die Dateigröße — es weiß also, welche Container gefährlich sind, und sagt es niemandem. Der Vorfallsbericht nennt das Signal 4 und ordnet es richtig ein: eine **Inventarprüfung**, kein Alarm im Ernstfall. Es meldet die Bombe, bevor sie zündet.

## Ziele

- Whiskers frisst nicht mehr sein eigenes Zugriffsprotokoll.
- Container ohne Log-Rotation sind bekannt, bevor ihr Log zum Problem wird.
- Die Meldung enthält den fertigen Behebungsweg, führt ihn aber nicht aus.

## Non-Goals

- **Keine** automatische Änderung der Log-Konfiguration. Das erfordert Neuerzeugen des Containers — und beim Docker-Proxy kappt genau das den eigenen Steuerkanal.
- **Kein** Abschneiden fremder Logdateien.
- **Keine** allgemeine Compliance-/Härtungsprüfung. Nur die Prüfungen mit direktem Bezug zur Betriebssicherheit von Whiskers.
- **Keine** Änderung an der Alarm-Engine — die Befunde laufen über den vorhandenen Weg.

## Zielgruppen / Personas

### Flottenbetreiber

- Pain Point: Erfährt von einer 500-MB-Logdatei erst, wenn der Server steht.

### Whiskers-Betreiber mit gehärtetem Profil

- Kontext: Nutzt socket-proxy/ghostunnel laut `container-hardening.md`.
- Pain Point: Genau diese empfohlene Härtung hat den Vorfall ausgelöst — die Empfehlung ist ohne diesen Fix unvollständig.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Container, die zum Zugriffspfad von Whiskers gehören (Docker-Proxy, mTLS-Tunnel), werden automatisch erkannt und vom Log-Scan ausgenommen. | Must |
| FR-02 | Die Erkennung stützt sich nicht allein auf Namen: der tatsächlich konfigurierte Zugriffspfad je Server wird ausgewertet; `SERVERWATCH_SELF_CONTAINERS` bleibt als Übersteuerung. | Must |
| FR-03 | Eine wiederkehrende Inventarprüfung (Default täglich) erfasst je Container: `HostConfig.LogConfig` und die Größe der Datei hinter `LogPath`. | Must |
| FR-04 | Befund „kein Rotationslimit **und** Log > Schwelle" (Default 100 MB) erzeugt einen Hinweis mit Server, Container, Größe, Wachstumsrate und dem fertigen `docker`-Befehl zur Behebung. | Must |
| FR-05 | Befund „kein Rotationslimit" allein erscheint als Inventar-Hinweis in der Oberfläche, nicht als Meldung. | Must |
| FR-06 | Wachstumsrate wird aus zwei aufeinanderfolgenden Prüfungen berechnet und in der Meldung als „MB/Tag" ausgewiesen. | Should |
| FR-07 | Ein Hinweis „Rotations-Default auf dem Host fehlt" (`/etc/docker/daemon.json`), wo ermittelbar. | Should |
| FR-08 | Die ausgeschlossenen Container aus FR-01 sind in der Oberfläche sichtbar — mit Begründung, warum sie nicht gescannt werden. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `get_log_hygiene_report` (read): Container ohne Rotationslimit, Loggröße, Wachstumsrate und der vorbereitete Behebungsbefehl als Text. **Kein** schreibendes Werkzeug — Neuerzeugen bleibt freigabepflichtig. | Must |

## Nicht-Funktionale Anforderungen

- **Billig:** Die Inventarprüfung darf nicht mehr als einen Aufruf je Container und Tag kosten und läuft unter dem Budget aus SP-1.
- **Kein Fehlausschluss:** Ein fälschlich ausgeschlossener Container ist ein blinder Fleck. FR-08 macht das Risiko sichtbar.
- **Keine Root-Anforderung:** Wo die Dateigröße ohne erhöhte Rechte nicht lesbar ist, wird das als „unbekannt" ausgewiesen, nicht geschätzt.

## User Stories

- **US-01:** Als Betreiber möchte ich erfahren, dass ein Container ohne Rotationslimit auf 300 MB gewachsen ist, bevor er 500 MB erreicht.
- **US-02:** Als Betreiber möchte ich den Behebungsbefehl vorbereitet bekommen und selbst entscheiden, wann ich ihn ausführe.
- **US-03:** Als Betreiber möchte ich sehen, welche Container Whiskers bewusst nicht scannt.

### Flow für US-01

```
Given ghostunnel schreibt 80 MB/Tag ohne Rotationslimit
When die tägliche Inventarprüfung läuft und 100 MB überschritten sind
Then erscheint ein Hinweis: "ghostunnel auf badwolf: 118 MB, kein Limit,
     ca. 80 MB/Tag — Behebung: logging.max-size=10m, max-file=3 setzen
     und den Container neu erzeugen"   [Befehl kopieren]
```

## Akzeptanzkriterien

- FR-01 bis FR-05 und FR-08 umgesetzt.
- Gegenprobe für FR-01: Auf einem Server mit socket-proxy/ghostunnel enthält ein „alle Container"-Log-Alarm nach der Änderung **keine** Treffer mehr aus diesen beiden — nachweisbar, indem eine Regel auf ein Muster gesetzt wird, das nur im Proxy-Log vorkommt.
- Gegenprobe für FR-04: Ein künstlich auf 150 MB gebrachtes Log ohne Rotationslimit erzeugt beim nächsten Prüflauf genau einen Hinweis mit korrekter Größenangabe.
- Der ausgegebene Behebungsbefehl ist wörtlich lauffähig (kopieren, einfügen, ausführen) — geprüft auf einem Testserver.
- MCP: Der Agent kann den Befund samt Behebungsbefehl wiedergeben, ohne ihn ausführen zu können.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Rückkopplung beendet | Zeilenrate im Proxy-Log vor/nach der Änderung | deutlich gesunken | unverändert ⇒ der Ausschluss greift nicht, die Schleife lebt |
| Ausgeschlossene Container | Ansicht FR-08 | genau die Zugriffspfad-Container | mehr als erwartet ⇒ Fehlausschluss, blinder Fleck |
| Log-Inventar | Ansicht FR-05 | vollständige Liste je Server | Lücken/„unbekannt" häufig ⇒ Rechte oder `LogPath` nicht lesbar |
| Vorlaufzeit | Zeit zwischen erstem Hinweis und kritischer Größe | > 3 Tage | < 1 Tag ⇒ Schwelle zu hoch angesetzt |
| Wachstumsrate | FR-06 gegen echte Messung `du -sh` | Abweichung < 20 % | größer ⇒ Berechnung oder Prüfintervall unbrauchbar |
| Befehlsqualität | Stichprobe: Befehl auf Testserver ausführen | läuft ohne Nacharbeit | Fehler ⇒ der Hinweis erzeugt Arbeit statt sie zu sparen |

## Woran ich sehe, dass es bricht

1. **Der Fehlausschluss ist der teure Fehler dieses Pakets.** Ein Container, der versehentlich als „Zugriffspfad" erkannt wird, verschwindet lautlos aus der Log-Überwachung — und niemand vermisst ihn. **Gegenmaßnahme und Messstelle:** FR-08 listet die Ausschlüsse dauerhaft sichtbar. Wächst diese Liste ohne Konfigurationsänderung, ist die Erkennung zu gierig.
2. **Namensbasierte Erkennung, die auf fremden Hosts danebengreift.** `socket-proxy` heißt anderswo anders, und umgekehrt kann ein Kundencontainer genauso heißen. Deshalb FR-02: der konfigurierte Zugriffspfad ist die Wahrheit, der Name die Krücke. **Gegenprobe:** ein Container, der zufällig `socket-proxy` heißt, aber nicht im Zugriffspfad liegt, wird weiter gescannt.
3. **Ein Hinweis, den niemand liest, ist kein Schutz.** Inventar-Hinweise landen leicht in einer Liste, die niemand öffnet. **Messstelle:** Zeit zwischen erstem Hinweis und Behebung. Bleibt sie über Wochen, muss der Hinweis eskalieren, sobald die Größe kritisch wird — Inventar reicht dann nicht.
4. **Die Schwelle misst die falsche Größe.** 100 MB sind auf einem großen Server harmlos und auf BurgCloud bereits spürbar. **Messstelle:** Logvolumen im Verhältnis zum freien Plattenplatz, nicht absolut.
5. **Symptombehandlung wird für Heilung gehalten.** Rotation kleiner Logs macht die Abrufe schnell — der Defekt aus SP-1/SP-2 bleibt bestehen und wird nur nicht mehr ausgelöst. Das steht wörtlich im Vorfallsbericht. **Konsequenz:** dieses Paket darf niemals als Ersatz für SP-1/SP-2 gelten, und die Meldungen müssen so formuliert sein, dass sie nicht diesen Eindruck erwecken.

## Do's

- **Den Zugriffspfad als Quelle nehmen**, nicht die Namensliste.
- **Ausschlüsse dauerhaft sichtbar machen.**
- **Den Befehl fertig ausgeben** — kopierbar, mit den konkreten Werten des Containers.
- **Nach Plattenanteil statt absoluter Größe bewerten.**

## Don'ts

- **Nicht** Log-Rotation automatisch setzen. Das Neuerzeugen von `ghostunnel` kappt den eigenen Steuerkanal — dieser Fall ist ausdrücklich im Vorfallsbericht benannt.
- **Nicht** fremde Logdateien abschneiden, auch nicht „nur die eigenen".
- **Nicht** den Selbstausschluss auf Verdacht erweitern. Jeder Ausschluss ist ein blinder Fleck.
- **Nicht** dieses Paket als Behebung des Vorfalls verbuchen. Es nimmt den Auslöser weg, nicht die Ursache.

## Abhängigkeiten

- **Wird blockiert von:** nichts. Startfähig, unabhängig von SP-1.
- **Verwandt:** `container-hardening.md` — der dort empfohlene Proxy-Aufbau ist ohne FR-01 unvollständig und sollte den Hinweis aufnehmen.

## Offene Fragen

- **F-01:** Wie wird der Zugriffspfad je Server maschinell ermittelt, wenn der Nutzer eine beliebige TCP-Adresse konfiguriert hat? Vorschlag: Abgleich der Zieladresse mit den Port-Bindings der Container dieses Servers.
- **F-02:** Soll der Hinweis bei kritischer Größe automatisch zur Meldung eskalieren? Vorschlag: ja, ab 25 % des freien Plattenplatzes.
