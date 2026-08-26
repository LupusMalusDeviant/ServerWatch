# Plan-0012: Reife & Vertrauen (GAP-5)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0012](../prd/0012-reife-und-vertrauen.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Drei Baustellen: eine überholte Positionierungsprämisse (MCP als Alleinstellungsmerkmal — Portainer und Coolify haben beide MCP-Server), ein öffentlich dokumentierter Vorfall ohne sichtbare Behebung, und fehlende Betriebsbelege (Kadenz, Aktualisierungspfad, Reaktionszeit).

Dieser Plan ist überwiegend Schreibarbeit, hat aber eine harte technische Abhängigkeit und eine strenge Reihenfolge: **Der Vorfall darf erst prominent verlinkt werden, wenn SP-1 und SP-2 umgesetzt sind.** Ehrlichkeit mit Behebung ist ein Vertrauensgewinn; Ehrlichkeit ohne ist ein dokumentiertes Ausschlusskriterium.

## Ziele

- Die Positionierung steht auf prüfbaren Aussagen.
- Ein Interessent findet in zehn Minuten Belege für Betreibbarkeit.
- Der Vorfall wirkt als Stärke.

## Arbeitspakete

### WP1: Prämisse korrigieren

**Zweck:** Eine falsche Grundannahme zerlegt jeden Vergleich.
**Schätzung:** S (1 Tag).

1. **WP1.1:** Fundstellen suchen: `grep -rin "alleinstellung\|unique\|einzigartig" docs/ README.md` plus die Website-Texte.
2. **WP1.2:** Aussage umstellen von „wir haben MCP" auf **„regierte Autonomie"**: Agent im Produkt, Guardrail-Engine, Freigabe-Ablauf in der eigenen Oberfläche, Auslöser, korrelierte Nachweiskette. Der Unterschied zu einem MCP-Adapter vor der REST-API wird benannt, nicht verschwiegen.
3. **WP1.3:** `missingFeatures.md`, `beatPortainerCoolify.md`, `product/POSITIONING.md`, README und Website angleichen.

**Abnahme:** Volltextsuche findet keine Behauptung mehr, MCP sei ein Alleinstellungsmerkmal.

### WP2: Ehrliche Vergleichstabelle

**Zweck:** Nachprüfbarkeit statt Werbung.
**Schätzung:** M (1,5 Tage).

1. **WP2.1:** Tabelle mit „besser" **und** „schlechter", je Zeile mit Quelle und Datum.
2. **WP2.2:** **Ablaufdatum im Dokument** (Vorschlag 6 Monate). Nach Ablauf wird überprüft oder zurückgezogen — keine dritte Option. Genau dieses Versäumnis hat die MCP-Prämisse veralten lassen.
3. **WP2.3:** Vergleichspunkte: Reife, Kubernetes, PaaS/Git-Deploy, Monitoring-Tiefe, Uptime/Kanäle, HA, Betriebssicherheit — plus die Stärken.

**Abnahme:** Eine unbeteiligte Person liest die Tabelle als Einordnung, nicht als Werbung.

### WP3: Vorfall abschließen und zeigen

**Zweck:** Aus dem größten Risiko den stärksten Beleg machen.
**Schätzung:** S (1 Tag). **Erst nach SP-1 und SP-2.**

1. **WP3.1:** Vorfallsbericht um einen Abschnitt „Behebung" ergänzen: Commits, der Invariantentest aus Plan-0001 WP6, die Messstellen, an denen ein Rückfall auffiele.
2. **WP3.2:** Aus README und CHANGELOG verlinken.
3. **WP3.3:** Den Bericht **inhaltlich unverändert** lassen. Seine Klarheit ist der Wert.

**Abnahme:** Der Weg vom Bericht zur Behebung und zum ausschließenden Test ist in zwei Klicks nachvollziehbar.

### WP4: Betriebsbelege

**Zweck:** Die Fragen beantworten, die vor einer Einführung gestellt werden.
**Schätzung:** M (2 Tage).

1. **WP4.1:** Release-Kadenz zusagen — **haltbar** formuliert. Vorschlag: „Nebenversionen bei Bedarf, Sicherheitsfixes innerhalb von 7 Tagen".
2. **WP4.2:** Aktualisierungsanleitung 0.x → 0.x+1 mit Datenbankmigration und Rückweg.
3. **WP4.3:** **Die Anleitung an einer Kopie echter Daten durchspielen**, inklusive Rückweg — je Release, nicht einmalig.
4. **WP4.4:** Reaktionszusage in `SECURITY.md` mit Zeitangabe.
5. **WP4.5:** Seite „Bekannte Grenzen": Einzelinstanz, Kubernetes-Umfang, was Whiskers ausdrücklich nicht tut.

**Abnahme:** Eine echte Aktualisierung auf einer Datenkopie läuft samt Rückweg durch, protokolliert.

### WP5: Erster Eindruck

**Zweck:** Die zehn Minuten, in denen entschieden wird.
**Schätzung:** M (2 Tage).

1. **WP5.1:** Screenshots neu aufnehmen, englische Oberfläche, hell und dunkel.
2. **WP5.2:** Platzhalterbilder der In-App-Hilfe durch echte Aufnahmen ersetzen.
3. **WP5.3:** Demo-Modus mit Beispieldaten (baut auf `FakeWorkloadProvider`), damit ein Interessent ohne eigene Server etwas sieht.
4. **WP5.4:** **Fremdtest:** Eine Person ohne Vorwissen liest zehn Minuten und beantwortet: Wofür ist es? Was kann es nicht? Wie oft gibt es Releases?

**Abnahme:** Alle drei Fragen im Fremdtest korrekt beantwortet.

### WP-MCP: Werkzeugkatalog veröffentlichen

**Zweck:** Dieses Paket liefert selbst keine Werkzeuge — es macht den Katalog zum Beleg der Positionierung. Siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) FR-10.
**Schätzung:** S (0,5 Tage).

1. **WP-MCP.1:** Den aus Plan-0013 WP3 erzeugten Katalog (Werkzeug, Stufe, Modul, Kurzbeschreibung) als Dokumentationsseite veröffentlichen — Portainer veröffentlicht seinen ebenfalls.
2. **WP-MCP.2:** Aus der Positionierung heraus verlinken: Der Katalog ist der prüfbare Teil der Aussage „regierte Autonomie“ — Werkzeuge **mit** Stufen, Freigaben und Nachweiskette.
3. **WP-MCP.3:** In die „Bekannte Grenzen“-Seite (WP4.5) aufnehmen, welche Vorgänge dem Agenten bewusst **nicht** offenstehen (Entsperrung nach Schutzabschaltung, Zwei-Personen-Freigaben).

**Abnahme:** Der veröffentlichte Katalog stimmt mit `tools/list` des ausgelieferten Servers überein; ein Leser kann die bewussten Auslassungen benennen.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2
SP-1 + SP-2 (extern, zwingend) ──> WP3
WP4 unabhängig
WP5 zuletzt (braucht die aktuelle Oberfläche)
```

- **Extern blockiert von:** SP-1 und SP-2 für WP3.
- **Läuft parallel zu allem anderen**, außer WP3.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Prämissen-Aktualität | Volltextsuche | keine überholten Aussagen |
| Einhaltung der Kadenz | Release-Historie gegen Zusage | eingehalten |
| Alter der Vergleichstabelle | Datum im Dokument | < 6 Monate |
| Aktualisierungspfad | Lauf auf Datenkopie je Release | funktioniert samt Rückweg |
| Fremdtest | halbjährlich mit einer neuen Person | drei Fragen korrekt |
| Screenshot-Aktualität | Abgleich mit der laufenden Version | passend |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Zusage nicht gehalten | stärkeres Signal für ein totes Projekt als gar keine Zusage | haltbar formulieren (WP4.1); zweimal verfehlt ⇒ ändern, nicht ignorieren |
| Anleitung nie ausprobiert | scheitert beim Nutzer mit dessen Daten | WP4.3 als verpflichtender Lauf je Release |
| Vergleich altert still | genau das ist mit der MCP-Prämisse passiert | Ablaufdatum in WP2.2 |
| Vorfall ohne Behebung beworben | dokumentiertes Ausschlusskriterium | WP3 erst nach SP-1/SP-2 |
| Text erklärt die Technik statt den Nutzen | von innen nicht feststellbar | WP5.4 Fremdtest mit einer unbeteiligten Person |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 | Volltextsuche sauber |
| M2 | WP2 | Tabelle mit Quellen und Ablaufdatum |
| M3 | WP4 | Aktualisierung auf Datenkopie protokolliert |
| M4 | WP3 | Vorfall mit Behebung verlinkt (nach SP-1/SP-2) |
| M5 | WP5 | Fremdtest bestanden |
| M6 | Kadenz | drei Zyklen eingehalten — **erst dann gilt GAP-5 als abgeschlossen** |

## Rückweg

Reine Dokumentationsarbeit; jeder Schritt ist einzeln zurücknehmbar. Der einzige nicht rücknehmbare Teil ist die öffentliche Sichtbarkeit des Vorfalls — deshalb die harte Reihenfolge nach SP-1/SP-2.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [ ] Keine Fundstelle behauptet mehr, MCP sei ein Alleinstellungsmerkmal
- [ ] Vergleichstabelle mit Quellen, Datum und Ablaufdatum
- [ ] Vorfall aus README und CHANGELOG erreichbar, mit Behebung und ausschließendem Test
- [ ] Aktualisierung 0.x → 0.x+1 auf einer Kopie echter Daten durchgespielt, inklusive Rückweg
- [ ] „Bekannte Grenzen"-Seite vorhanden
- [ ] Fremdtest bestanden: Zweck, Grenzen und Kadenz korrekt wiedergegeben
- [ ] Zugesagte Kadenz über drei aufeinanderfolgende Zyklen eingehalten
- [ ] Werkzeugkatalog veröffentlicht, mit `tools/list` des ausgelieferten Servers deckungsgleich und aus der Positionierung verlinkt
