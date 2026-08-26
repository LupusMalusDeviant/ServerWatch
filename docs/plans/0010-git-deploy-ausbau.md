# Plan-0010: Git-Deploy-Ausbau (GAP-3)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0010](../prd/0010-git-deploy-ausbau.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

F5 liefert Klonen, Bauen, Starten. Es fehlt der Weg von „läuft" zu „erreichbar", der Rückweg bei einem kaputten Deploy und der SSH-Deploy-Key für private Repos.

Der Plan hat eine bewusste Reihenfolge: **erst der Negativpfad, dann die Bequemlichkeit.** Rücksprung und Gesundheitsprüfung sind das, was den Unterschied zwischen einem Werkzeug und einem Risiko ausmacht.

Und er hat eine Warnung mit realem Präzedenzfall: Der Live-`Caddyfile` der Whiskers-Website routet auch ein zweites, unbeteiligtes Produkt. Ein Konfigurationseingriff, der die Datei als Ganzes schreibt, hätte es abgeschaltet. Jeder Proxy-Eingriff in diesem Plan ist danach zu bemessen.

## Ziele

- Ein Deploy endet mit einer erreichbaren HTTPS-URL.
- Ein kaputter Deploy springt automatisch zurück.
- Private Repos ohne Token sind nutzbar.

## Arbeitspakete

### WP1: Rücksprung und Deploy-Historie

**Zweck:** Der Negativpfad zuerst.
**Schätzung:** M (2 Tage).

1. **WP1.1:** Deploy-Historie je Anwendung: Git-SHA, Zeitpunkt, Auslöser, Ergebnis.
2. **WP1.2:** Aufbewahrung der letzten n Build-Images auf dem Zielserver (Default 3), mit sichtbarem Plattenverbrauch.
3. **WP1.3:** Ein-Klick-Rücksprung auf einen früheren erfolgreichen Deploy — das Image liegt bereits als `whiskers-build/<app>:<gitsha>` vor.
4. **WP1.4:** **Prüfung, ob der Rücksprung überhaupt möglich ist:** Fehlt das Image, wird das in der Oberfläche angezeigt, statt einen Knopf anzubieten, der ins Leere führt.

**Abnahme:** Erzwungener Fehlstart ⇒ Rücksprung ⇒ die vorherige Version antwortet unter der URL.

### WP2: Gesundheitsprüfung nach dem Deploy

**Zweck:** „Erfolgreich" an Erreichbarkeit binden, nicht an den Rückgabewert.
**Schätzung:** M (2 Tage).

1. **WP2.1:** Nach dem Start prüfen, ob der Dienst unter der Zieladresse antwortet — mit der Prüf-Engine aus Plan-0009 WP1.
2. **WP2.2:** Wartezeit und Wiederholungen konfigurierbar (Anwendungen brauchen unterschiedlich lange).
3. **WP2.3:** Ohne bestandene Prüfung gilt der Deploy als fehlgeschlagen und löst den Rücksprung aus.
4. **WP2.4:** Kennzahl: Anteil der als „erfolgreich" geführten Deploys, deren Prüfung fehlschlug — muss null sein.

**Abnahme:** Ein Deploy einer Anwendung, die nicht startet, wird als fehlgeschlagen geführt und zurückgesprungen.

### WP3: Domain, Proxy und TLS

**Zweck:** Der Weg zur erreichbaren URL.
**Schätzung:** L (4 Tage) — der heikelste Teil.

1. **WP3.1:** Domainzuordnung je Deploy (Hostname, Zielport im Container).
2. **WP3.2:** Proxy-Regel über den vorhandenen `NginxService`, **ausschließlich in klar markierten eigenen Blöcken**, idempotent, mit Abschnittsmarkierungen im Dateiinhalt.
3. **WP3.3:** **Sicherung vor jedem Eingriff:** Prüfsumme über alle nicht von Whiskers verwalteten Blöcke vor und nach der Änderung. Abweichung ⇒ Abbruch und Wiederherstellung.
4. **WP3.4:** Zertifikat über `SslCertService`, inklusive Erneuerung; Restlaufzeit als Dauerkennzahl (gemeinsam mit Plan-0009 WP2.3).
5. **WP3.5:** Abstraktion so schneiden, dass Caddy/Traefik später ergänzt werden können, ohne den Aufrufer zu ändern.

**Abnahme:** Fremde Blöcke nachweislich unverändert (Prüfsummenvergleich); die URL antwortet mit gültigem Zertifikat.

### WP4: SSH-Deploy-Keys

**Zweck:** Private Repos ohne Token.
**Schätzung:** M (2 Tage).

1. **WP4.1:** Schlüsselpaar in Whiskers erzeugen, privaten Teil im Vault, öffentlichen Teil zum Kopieren anzeigen.
2. **WP4.2:** Auf dem Zielserver als temporäre 0600-Datei bereitstellen, analog zum bestehenden `GIT_ASKPASS`-Muster aus F5 — **nie** über `argv`.
3. **WP4.3:** Aufräumen nach dem Build garantieren, auch im Fehlerfall.
4. **WP4.4:** Bekannte Host-Schlüssel für die üblichen Anbieter mitliefern; unbekannte Hosts erfordern eine bewusste Bestätigung.

**Abnahme:** Klonen eines privaten Repos ohne Token; nach dem Build existiert keine Schlüsseldatei mehr auf dem Server.

### WP5: Build-Last und Protokolle

**Zweck:** Der Build darf den Server nicht in den Zustand vom 26.08. bringen.
**Schätzung:** S (1 Tag).

1. **WP5.1:** Höchstens ein Build je Server gleichzeitig.
2. **WP5.2:** Während eines Builds erzeugte Lastalarme als „Build läuft" kennzeichnen, nicht unterdrücken.
3. **WP5.3:** Build-Protokoll live und aufbewahrt je Deploy.
4. **WP5.4:** Geheimnisfilter über das gespeicherte Protokoll; Gegenprobe per Suche nach dem Tokenwert.

**Abnahme:** Kein Geheimnis im gespeicherten Protokoll; Lastalarme während eines Builds sind als solche erkennbar.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `list_deployments`, `get_deploy_log`, `trigger_deploy`, `rollback_deploy` — Deploy-Historie lesen, deployen und zurückspringen. Stufe: read / write, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** `GitDeployModule` liefert danach nicht mehr `Array.Empty<Type>()`. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2 ──> WP3
WP4 unabhängig, parallel möglich
WP5 begleitend ab WP1
```

- **Extern blockiert von:** SP-1 (Build-Last), Plan-0009 WP1 (Prüf-Engine für WP2).
- **Baut auf:** F5 (Git-Deploy) und F11 (Webhook-Geheimnisse), beide vorhanden.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Zeit bis erreichbar | Stoppuhr, frischer Server | < 10 min |
| Rücksprung möglich | vorhandene Build-Images je Anwendung | ≥ 1 zusätzlich zur laufenden |
| Fremdkonfiguration | Prüfsumme vor/nach (WP3.3) | unverändert |
| „Erfolgreich" trotz fehlgeschlagener Prüfung | Kennzahl WP2.4 | null |
| Geheimnisse im Protokoll | Suche nach Tokenwert | keine Treffer |
| Zertifikats-Restlaufzeit | Kennzahl WP3.4 | keine unter 7 Tagen |
| Host-Last während Build | SP-4 | Alarme als „Build" gekennzeichnet |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Proxy-Eingriff trifft fremde Dienste | ein unbeteiligtes Produkt geht offline | WP3.3 Prüfsummenvergleich, verpflichtend vor jedem Merge; nur markierte eigene Blöcke |
| „Erfolgreich" ohne Erreichbarkeit | derselbe Fehler wie in SP-6 | WP2.3 + Kennzahl WP2.4 |
| Rücksprung ins Leere | fällt erst im Ernstfall auf | WP1.2 Aufbewahrung + WP1.4 Vorabprüfung |
| Build frisst den Server | Lastbild des Vorfalls | WP5.1 + Kennzeichnung statt Unterdrückung |
| Zertifikat läuft still ab | Dienst offline lange nach dem Deploy | Restlaufzeit als Dauerkennzahl, nicht als einmalige Prüfung |
| Schlüsseldatei bleibt liegen | dauerhafter Zugang auf dem Zielserver | WP4.3 mit Aufräumtest, auch im Fehlerpfad |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 + WP2 | erzwungener Fehlstart ⇒ automatischer Rücksprung ⇒ alte Version antwortet |
| M2 | WP3 | erreichbare HTTPS-URL; fremde Blöcke prüfsummengleich |
| M3 | WP4 | privates Repo ohne Token; keine Schlüsselreste |
| M4 | WP5 | kein Geheimnis im Protokoll; Build-Alarme gekennzeichnet |
| M5 | Gesamtlauf | frischer Server: Repo → HTTPS-URL in unter 10 Minuten, gemessen |

## Rückweg

Der bestehende F5-Pfad bleibt unverändert nutzbar; alle Erweiterungen sind je Anwendung ein- und ausschaltbar. Der Proxy-Eingriff ist die einzige Änderung an fremden Systemen und lässt sich vollständig deaktivieren — dann bleibt der heutige Zustand mit Handarbeit.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [ ] Frischer Server: Repo → erreichbare HTTPS-URL in unter 10 Minuten, Zeit gemessen
- [ ] Erzwungener Fehlstart führt zu automatischem Rücksprung, alte Version antwortet
- [ ] Prüfsumme über fremde Proxy-Blöcke vor/nach unverändert
- [ ] Kein Treffer bei der Geheimnissuche im gespeicherten Build-Protokoll
- [ ] Privates Repo per Deploy-Key geklont, keine Schlüsselreste auf dem Server
- [ ] Kennzahl „erfolgreich trotz fehlgeschlagener Prüfung" ist null
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
