# PRD-0010: Git-Deploy-Ausbau (GAP-3)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** Betreiber, die Anwendungen aus Git betreiben
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — GAP-3; baut auf `missingFeatures.md` F5 auf
- **Ersetzt:** —

## Problem / Motivation

F5 hat einen funktionierenden Git-Deploy-Pfad geliefert: Repo klonen, bauen, hochfahren — auf dem Zielserver, mit Token aus dem Vault, ausgelöst per Webhook. Er ist bewusst schlank gehalten und hat drei Einschränkungen, die im direkten Vergleich schmerzen:

1. **Nur https-Remotes.** Kein SSH-Deploy-Key-Weg — ausgerechnet in einem Produkt, dessen Kernversprechen der SSH-key-freie Betrieb ist, ist das erklärungsbedürftig, aber es schließt private Repos ohne Token aus.
2. **Kein Weg von „läuft" zu „erreichbar".** Nach dem Deploy hat der Betreiber einen laufenden Container und muss Domain, Reverse-Proxy-Regel und TLS selbst einrichten. Coolify und Dokploy erledigen genau das — Traefik-Routing, Domainverwaltung, Zertifikate — und das ist der Grund, warum sie als „self-hosted Heroku" wahrgenommen werden.
3. **Kein Rückweg.** Schlägt ein Deploy fehl oder liefert eine kaputte Version, gibt es keinen Ein-Klick-Weg zurück auf die vorherige.

Der Vergleich ist unbarmherzig: Coolify hat über 280 Ein-Klick-Dienste, Dokploy 80–100. Diesen Wettlauf gewinnt Whiskers nicht, und er ist auch nicht das Ziel. Aber der Weg von „ich habe ein Repo" zu „es läuft unter meiner Domain mit gültigem Zertifikat" ist der eine Ablauf, an dem Nutzer die Kategorie messen.

## Ziele

- Ein Deploy endet mit einem unter der gewünschten Domain erreichbaren Dienst, nicht mit einem laufenden Container.
- Ein fehlgeschlagener oder kaputter Deploy ist mit einem Klick rückgängig zu machen.
- Private Repos ohne Token sind nutzbar.

## Non-Goals

- **Keine** Buildpack-/Nixpacks-Automatik. Ein `Dockerfile` oder eine Compose-Datei im Repo bleibt Voraussetzung.
- **Kein** Katalog mit hunderten Ein-Klick-Diensten.
- **Keine** Vorschau-Umgebungen je Pull-Request.
- **Keine** eigene Build-Farm oder verteilte Builds. Gebaut wird auf dem Zielserver, wie bisher.
- **Kein** Ersatz für den vorhandenen Compose-Deploy-Pfad.

## Zielgruppen / Personas

### Betreiber eigener Anwendungen

- Kontext: betreibt zwei bis zehn selbstgeschriebene Dienste aus Git.
- Pain Point: Nach dem Deploy folgt Handarbeit an Proxy und Zertifikat — jedes Mal.

### Betreiber im Vergleichstest

- Kontext: probiert Whiskers gegen Coolify aus.
- Pain Point: Bricht ab, wenn nach dem Deploy kein erreichbarer Dienst steht.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | SSH-Deploy-Keys als zweiter Authentifizierungsweg: Schlüsselpaar wird in Whiskers erzeugt, der private Teil bleibt im Vault, der öffentliche wird zum Kopieren angezeigt. | Must |
| FR-02 | Domainzuordnung je Deploy: gewünschter Hostname, Zielport im Container. | Must |
| FR-03 | Automatische Reverse-Proxy-Regel auf dem Zielserver (bestehender `NginxService`), idempotent und ohne bestehende Regeln zu überschreiben. | Must |
| FR-04 | TLS-Zertifikat über den vorhandenen `SslCertService`, inklusive Erneuerung und Ablaufwarnung (Verknüpfung mit GAP-2 FR-03). | Must |
| FR-05 | Deploy-Historie je Anwendung mit Git-SHA, Zeitpunkt, Auslöser, Ergebnis. | Must |
| FR-06 | Ein-Klick-Rücksprung auf einen früheren erfolgreichen Deploy (Image-Tag ist bereits `whiskers-build/<app>:<gitsha>`). | Must |
| FR-07 | Gesundheitsprüfung nach dem Deploy: Der Deploy gilt erst als erfolgreich, wenn der Dienst unter der Domain antwortet — sonst automatischer Rücksprung. | Must |
| FR-08 | Build-Protokoll live verfolgbar, mit Aufbewahrung je Deploy. | Should |
| FR-09 | Umgebungsvariablen je Anwendung, Geheimnisse im Vault, niemals im Build-Protokoll sichtbar. | Must |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** `list_deployments` und `get_deploy_log` (read), `trigger_deploy` und `rollback_deploy` (write, mit Freigabe). `GitDeployModule` liefert heute **null** Werkzeuge — diese Lücke wird hier geschlossen (PRD-0013 FR-06). | Must |

## Nicht-Funktionale Anforderungen

- **Kein Fremdzugriff auf bestehende Konfiguration:** Der Proxy-Eingriff darf ausschließlich eigene Blöcke anlegen und ändern. Fremde Servernamen bleiben unangetastet — der Live-`Caddyfile` der eigenen Website, der ein zweites Produkt mitroutet, ist die stehende Warnung.
- **Build-Last unter Budget:** Ein Build darf den Zielserver nicht in den Zustand bringen, den SP-1 verhindern soll. Builds laufen mit begrenzter Parallelität, höchstens einer je Server.
- **Geheimnisse:** kein Token, kein Schlüssel in `argv`, Protokollen oder Umgebungsdumps.

## User Stories

- **US-01:** Als Betreiber möchte ich ein privates Repo per Deploy-Key anbinden, ohne einen Token zu erzeugen.
- **US-02:** Als Betreiber möchte ich nach dem Deploy eine erreichbare HTTPS-URL haben, ohne den Proxy anzufassen.
- **US-03:** Als Betreiber möchte ich nach einem kaputten Deploy in einem Klick auf die letzte funktionierende Version zurück.

### Flow für US-02

```
Given ein Repo mit Dockerfile und die Domain app.example.org
When der Deploy läuft
Then wird gebaut, gestartet, eine Proxy-Regel angelegt, ein Zertifikat geholt,
     und der Deploy gilt erst als erfolgreich, wenn https://app.example.org antwortet —
     andernfalls läuft automatisch der Rücksprung
```

## Akzeptanzkriterien

- FR-01 bis FR-07 und FR-09 umgesetzt.
- Vollständiger Ablauf auf einem frischen Server: Repo → erreichbare HTTPS-URL, ohne Handarbeit, in unter 10 Minuten. Zeit gemessen, nicht geschätzt.
- Negativfall: Ein Deploy, dessen Anwendung nicht startet, führt zu automatischem Rücksprung, und die vorherige Version ist danach wieder erreichbar — nachgewiesen durch Abruf der URL.
- Der Proxy-Eingriff lässt eine vorhandene fremde Konfiguration nachweislich unverändert (Prüfsumme vorher/nachher über die fremden Blöcke).
- Kein Geheimnis im Build-Protokoll — geprüft durch Suche nach dem Tokenwert im gespeicherten Protokoll.
- MCP: Der Agent kann einen fehlgeschlagenen Deploy benennen und nach Freigabe zurückspringen; beide Aufrufe stehen in der Wirkungskontrolle.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Zeit bis erreichbar | Stoppuhr auf frischem Server | < 10 min | > 20 min ⇒ der Ablauf verliert seinen Zweck |
| Rücksprung funktioniert | Abruf der URL nach erzwungenem Fehler | vorherige Version antwortet | Fehlerseite ⇒ FR-06/FR-07 unbrauchbar |
| Fremdkonfiguration unberührt | Prüfsumme über fremde Proxy-Blöcke | unverändert | verändert ⇒ **sofortiger Stopp**, das Paket kann fremde Dienste abschalten |
| Geheimnisse im Protokoll | `grep` nach Tokenwert in gespeicherten Protokollen | keine Treffer | Treffer ⇒ Vault-Nutzung unterlaufen |
| Build-Last | Host-CPU während des Builds (SP-4) | Meldung erwartet und angekündigt | unangekündigte Lastalarme ⇒ Build und Monitoring arbeiten gegeneinander |
| Zertifikatserneuerung | Testzertifikat kurz vor Ablauf | automatisch erneuert | abgelaufen ⇒ der Dienst fällt aus, ohne dass ein Deploy stattfand |

## Woran ich sehe, dass es bricht

1. **Der Proxy-Eingriff trifft fremde Dienste.** Das ist der teuerste denkbare Fehler dieses Pakets: eine Regeländerung, die einen anderen, nicht beteiligten Dienst vom Netz nimmt. Der reale Präzedenzfall existiert bereits — der Live-`Caddyfile` der Whiskers-Website routet auch ein zweites Produkt, und ein Drüberkopieren hätte es abgeschaltet. **Gegenprobe, verpflichtend vor jedem Merge:** Prüfsumme über alle nicht von Whiskers verwalteten Konfigurationsblöcke, vor und nach einem Deploy.
2. **„Erfolgreich" ohne Erreichbarkeit.** Ein Deploy, der als erfolgreich gilt, weil der Container läuft, ist derselbe Fehler wie in SP-6: Rückgabewert statt Wirkung. **Messstelle:** Anteil der Deploys mit Ergebnis „erfolgreich", bei denen die anschließende HTTP-Prüfung fehlschlug. Größer null ⇒ FR-07 wird umgangen.
3. **Rücksprung, der nichts zurückspringt.** Wenn das alte Image bereits weggeräumt wurde, führt der Klick ins Leere — und zwar erst im Ernstfall. **Messstelle:** je Anwendung prüfen, ob die letzten n Build-Images auf dem Zielserver noch vorhanden sind. Fehlen sie, ist die Rücksprungfunktion eine Attrappe.
4. **Der Build frisst den Server.** Ein `docker build` auf einer 2-Kern-Maschine, während Whiskers dort seine Loops fährt, erzeugt genau das Lastbild aus dem Vorfall vom 26.08. **Messstelle:** Host-CPU während des Builds; Alarme, die dabei entstehen, müssen als „Build läuft" gekennzeichnet sein und dürfen nicht als Störung erscheinen.
5. **Zertifikate, die still ablaufen.** Ein Zertifikat, das nach 90 Tagen nicht erneuert wird, nimmt den Dienst offline, lange nachdem jemand an den Deploy gedacht hat. **Messstelle:** Restlaufzeit aller von Whiskers verwalteten Zertifikate als Dauerkennzahl, nicht als einmalige Prüfung beim Deploy.

## Do's

- **Erst der Negativpfad.** Rücksprung und Gesundheitsprüfung vor den Bequemlichkeitsfunktionen bauen.
- **Nur eigene Konfigurationsblöcke anfassen**, mit klarer Markierung im Dateiinhalt.
- **Nach dem Deploy von außen prüfen** — mit derselben Prüfmechanik wie GAP-2.
- **Die letzten n Build-Images vorhalten** und diese Zahl sichtbar machen.

## Don'ts

- **Nicht** in Richtung Dienstkatalog entwickeln. Der Wettlauf mit 280 Vorlagen ist verloren und war nie das Ziel.
- **Nicht** die Proxy-Konfiguration als Ganzes schreiben. Nur eigene Blöcke, idempotent.
- **Nicht** Builds parallel auf demselben Server zulassen.
- **Nicht** Geheimnisse über `argv` übergeben — der bestehende `GIT_ASKPASS`-Weg aus F5 bleibt das Muster.
- **Nicht** Vorschau-Umgebungen anfangen. Das ist ein eigenes Produkt.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 (Build-Last unter Budget), GAP-2 (die Erreichbarkeitsprüfung aus FR-07 nutzt dieselbe Mechanik).
- **Baut auf:** `missingFeatures.md` F5 (vorhanden), F11 (Webhook-Geheimnisse, vorhanden).

## Offene Fragen

- **F-01:** Reverse-Proxy — nur nginx (vorhanden) oder auch Caddy/Traefik? Vorschlag: nginx in v1, Erweiterung über eine Abstraktion, die von Anfang an vorgesehen ist.
- **F-02:** Wie viele Build-Images je Anwendung vorhalten? Vorschlag: 3, konfigurierbar, mit sichtbarem Plattenverbrauch.
