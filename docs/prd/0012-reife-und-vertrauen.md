# PRD-0012: Reife & Vertrauen (GAP-5)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Stakeholder:** potenzielle Nutzer im Vergleichstest, bestehende Nutzer, Mitwirkende
- **Roadmap:** [hardeningAndParity.md](../roadmap/hardeningAndParity.md) — GAP-5; präzisiert `beatPortainerCoolify.md`
- **Ersetzt:** —

## Problem / Motivation

Whiskers tritt gegen Portainer (zehn Jahre, dahinter eine Firma) und Coolify (über 50.000 GitHub-Sterne) an. Reife lässt sich nicht herbeiprogrammieren — aber die *Belege* für Verlässlichkeit lassen sich liefern, und genau daran wird ein unbekanntes Projekt gemessen.

Drei konkrete Befunde:

**Erstens** ist die strategische Prämisse in `missingFeatures.md` und `beatPortainerCoolify.md` überholt. Dort steht MCP als Alleinstellungsmerkmal. Inzwischen gibt es einen [offiziellen Portainer-MCP-Server](https://github.com/portainer/portainer-mcp) mit Read-only-Modus, Audit-Protokollierung aller mutierenden Aufrufe und Autorisierung über den Portainer-API-Key des jeweiligen Nutzers, sowie einen aktiv gepflegten Coolify-MCP-Server in der offiziellen Registry. Ein Positionierungstext, der auf einer falschen Prämisse steht, wird beim ersten Vergleich zerlegt.

Der tatsächliche Unterschied ist enger und dafür verteidigbar: Diese MCP-Server sind Adapter vor der REST-API — der Nutzer richtet ein externes Werkzeug darauf. Whiskers hat den Agenten **im Produkt**, mit Guardrail-Engine, Freigabe-Ablauf in der eigenen Oberfläche, Auslösern und korrelierter Nachweiskette. Die Aussage muss von „wir haben MCP" auf „regierte Autonomie" umgestellt werden.

**Zweitens** existiert seit dem 26.08. ein schriftlich dokumentierter Fall, in dem Whiskers einen Server sechs Tage lang lahmgelegt hat. Das ist die Sorte Geschichte, die eine Adoption beendet — und sie ist wahr, im Repo, und wird gefunden werden.

**Drittens** fehlen die üblichen Belege dafür, dass ein Projekt betreibbar ist: Zusage zur Release-Kadenz, Aktualisierungsanleitung zwischen Versionen, Reaktionszeit auf Sicherheitsmeldungen, ein nachvollziehbarer Umgang mit Fehlern.

## Ziele

- Die Positionierung steht auf einer prüfbaren Prämisse.
- Ein Interessent findet innerhalb weniger Minuten Belege dafür, dass das Projekt betreibbar ist.
- Der Vorfall vom 26.08. wird zum Vertrauensgewinn statt zum Totalschaden.

## Non-Goals

- **Keine** Marketing-Kampagne, kein Show-HN, keine Reichweitenaktion — dieses Paket schafft die Voraussetzungen dafür.
- **Keine** Feature-Parität mit Portainer oder Coolify.
- **Keine** kommerzielle Struktur (Support-Verträge, Enterprise-Version).
- **Keine** künstliche Aktivität (Stern-Kampagnen, aufgeblähte Commit-Historie).

## Zielgruppen / Personas

### Interessent im Vergleichstest

- Kontext: hat 30 Minuten, prüft drei Werkzeuge.
- Pain Point: Sucht Ausschlusskriterien, nicht Vorzüge. Findet er ein K.-o.-Merkmal, ist er weg.

### Betreiber vor der Einführung

- Pain Point: Muss beurteilen, ob das Projekt in zwölf Monaten noch existiert und ob Aktualisierungen funktionieren.

### Potenzieller Mitwirkender

- Pain Point: Will wissen, ob Beiträge angenommen werden und wie lange das dauert.

## Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FR-01 | Positionierungstexte (README, Website, `missingFeatures.md`, `beatPortainerCoolify.md`, `product/POSITIONING.md`) werden von „MCP als Alleinstellungsmerkmal" auf „regierte Autonomie" umgestellt, mit benanntem Vergleich zu den vorhandenen MCP-Servern der Wettbewerber. | Must |
| FR-02 | Eine ehrliche Vergleichstabelle im Repo, die auch benennt, wo Whiskers verliert — mit Datum und Nachprüfbarkeit. | Must |
| FR-03 | Der Vorfall vom 26.08. wird öffentlich behandelt: Bericht, Behebung, Test, der ihn ausschließt — verlinkt aus README und CHANGELOG. | Must |
| FR-04 | Zusage zur Release-Kadenz im README, mit tatsächlicher Einhaltung als Nachweis. | Must |
| FR-05 | Aktualisierungsanleitung zwischen Nebenversionen, inklusive Datenbankmigration und Rückweg. | Must |
| FR-06 | Reaktionszusage für Sicherheitsmeldungen in `SECURITY.md`, mit Zeitangabe. | Must |
| FR-07 | Eine „Bekannte Grenzen"-Seite: Einzelinstanz, Kubernetes-Umfang, was Whiskers ausdrücklich nicht tut. | Must |
| FR-08 | Screenshots und In-App-Handbuch auf dem aktuellen Stand, englisch. | Must |
| FR-09 | Ein reproduzierbarer Demo-Modus, in dem ein Interessent ohne eigene Server etwas sieht. | Should |
| FR-MCP | **MCP-Werkzeuge (siehe [PRD-0013](0013-mcp-und-agentenoberflaeche.md)):** Der Werkzeugkatalog mit Stufen wird als Teil der Dokumentation veröffentlicht (PRD-0013 FR-10) — Portainer veröffentlicht seinen ebenfalls; ein unveröffentlichter Katalog schwächt die Aussage „regierte Autonomie“ im direkten Vergleich. | Must |

## Nicht-Funktionale Anforderungen

- **Nachprüfbarkeit vor Vollständigkeit:** Jede Aussage im Vergleich muss mit Datum und Quelle belegbar sein. Eine überholte Aussage ist schädlicher als eine fehlende.
- **Selbstkritik ohne Selbstzerstörung:** Der Vorfallsbericht bleibt in seiner Klarheit, wird aber immer zusammen mit der Behebung und dem ausschließenden Test gezeigt.
- **Wartbarkeit:** Die Vergleichstabelle braucht ein Ablaufdatum. Ein veralteter Vergleich ist schlimmer als keiner.

## User Stories

- **US-01:** Als Interessent möchte ich in fünf Minuten erkennen, wofür Whiskers gedacht ist und wofür nicht.
- **US-02:** Als Betreiber möchte ich vor der Einführung wissen, wie Aktualisierungen ablaufen und wie ich zurückkomme.
- **US-03:** Als Interessent möchte ich sehen, wie das Projekt mit einem eigenen Fehler umgegangen ist — das sagt mehr als jede Funktionsliste.

### Flow für US-03

```
Given ein Interessent findet den Vorfallsbericht
When er dem Verweis folgt
Then sieht er: Ursachenanalyse, die Behebung mit Commit,
     den Test, der den Zustand ausschließt, und die Messstelle,
     an der ein Rückfall auffallen würde
```

## Akzeptanzkriterien

- FR-01 bis FR-08 umgesetzt.
- Keine Fundstelle im Repo oder auf der Website behauptet mehr, MCP sei ein Alleinstellungsmerkmal — geprüft durch Volltextsuche nach den einschlägigen Formulierungen.
- Fremdtest: Eine Person ohne Vorwissen beantwortet nach zehn Minuten Lesen korrekt, wofür Whiskers gedacht ist, was es nicht kann und wie oft es Releases gibt.
- Der Vorfallsbericht ist aus README und CHANGELOG erreichbar und zeigt Behebung plus Test.
- Die angekündigte Release-Kadenz wurde über drei aufeinanderfolgende Zyklen eingehalten — bevor GAP-5 als abgeschlossen gilt.
- MCP: Der veröffentlichte Katalog stimmt mit `tools/list` des ausgelieferten Servers überein und ist aus der Positionierung heraus verlinkt.

## Prüf- und Messstellen

| Was | Wo gemessen | Grün | Rot |
|---|---|---|---|
| Prämissen-Aktualität | Volltextsuche nach überholten Aussagen | keine Treffer | Treffer ⇒ die Positionierung widerlegt sich beim ersten Vergleich |
| Einhaltung der Kadenz | Release-Historie gegen Zusage | eingehalten | verfehlt ⇒ die Zusage schadet mehr, als sie nützt |
| Verständlichkeit | Fremdtest mit einer unbeteiligten Person | Kernfragen korrekt beantwortet | falsche Antworten ⇒ der Text erklärt sich selbst, nicht das Produkt |
| Aktualisierungspfad | echte Aktualisierung 0.x → 0.x+1 auf einer Kopie | funktioniert samt Rückweg | scheitert ⇒ der gefährlichste stille Fehler dieses Pakets |
| Alter der Vergleichstabelle | Datum im Dokument | < 6 Monate | älter ⇒ zurückziehen statt stehen lassen |
| Screenshot-Aktualität | Abgleich mit der laufenden Version | passend | veraltet ⇒ wirkt wie ein aufgegebenes Projekt |

## Woran ich sehe, dass es bricht

1. **Eine Zusage, die nicht gehalten wird, ist schlechter als keine.** „Monatliche Nebenversionen" im README und dann vier Monate Stille ist ein stärkeres Signal für ein totes Projekt als gar keine Aussage. **Messstelle:** Abstand zwischen Releases gegen die Zusage. Wird sie zweimal verfehlt, gehört sie geändert, nicht ignoriert.
2. **Die Aktualisierungsanleitung, die nie ausprobiert wurde.** Sie wird erst beim Nutzer benutzt — und scheitert dort, mit dessen Daten. **Gegenprobe, verpflichtend je Release:** eine echte Aktualisierung von der Vorversion auf einer Kopie echter Daten, inklusive Rückweg. Eine Anleitung ohne diesen Lauf ist eine Vermutung.
3. **Der Vergleich altert im Stillen.** Genau das ist mit der MCP-Prämisse passiert: sie war bei der Formulierung richtig und ist es heute nicht mehr, und niemand hat es gemerkt. **Gegenmaßnahme:** Ablaufdatum im Dokument; nach Ablauf wird es zurückgezogen oder überprüft — keine dritte Option.
4. **Ehrlichkeit ohne Behebung wird zur Waffe.** Der Vorfallsbericht ist ein Vertrauensgewinn, solange Behebung und ausschließender Test danebenstehen. Ohne sie ist er ein dokumentiertes Ausschlusskriterium. **Prüfstelle:** Der Verweis auf den Bericht darf erst prominent gesetzt werden, wenn SP-1 und SP-2 umgesetzt und im CHANGELOG belegt sind. Reihenfolge ist hier alles.
5. **Der Text erklärt die Technik statt das Problem.** Ein häufiger Fehler bei selbstgeschriebener Positionierung. Er lässt sich nur von außen feststellen. **Messstelle:** der Fremdtest. Wenn eine unbeteiligte Person nach zehn Minuten nicht sagen kann, wofür das Werkzeug gut ist, ist der Text gescheitert — unabhängig davon, wie richtig er ist.

## Do's

- **Erst SP-1/SP-2, dann den Vorfall prominent verlinken.** Ehrlichkeit mit Behebung ist ein Gewinn, Ehrlichkeit ohne ist ein Eigentor.
- **Die überholte Prämisse aktiv korrigieren**, statt sie stehen zu lassen und zu hoffen.
- **Grenzen aufschreiben** (FR-07). Genannte Grenzen schaffen Vertrauen, gefundene zerstören es.
- **Jede Vergleichsaussage datieren.**
- **Den Fremdtest wirklich durchführen**, mit einer Person, die das Projekt nicht kennt.

## Don'ts

- **Nicht** eine Kadenz zusagen, die nicht durchgehalten werden kann. Lieber „unregelmäßig, Sicherheitsfixes sofort" — das ist haltbar.
- **Nicht** den Vorfallsbericht entschärfen oder verschieben. Seine Klarheit ist der Wert.
- **Nicht** mit MCP als Alleinstellungsmerkmal werben. Das ist prüfbar falsch.
- **Keine** Vergleichstabelle, die nur Vorzüge zeigt. Sie wird sofort als Werbung gelesen.
- **Nicht** vor Abschluss von Welle 1 und 2 der Roadmap in die Öffentlichkeit gehen.

## Abhängigkeiten

- **Wird blockiert von:** SP-1 und SP-2 (für FR-03; ohne Behebung darf der Vorfall nicht beworben werden).
- **Verwandt:** `beatPortainerCoolify.md` Phase 0 — dieses PRD ersetzt dort die Prämisse und ergänzt L4.

## Offene Fragen

- **F-01:** Welche Kadenz ist realistisch für einen Entwickler neben dem Hauptberuf? Vorschlag: „Nebenversionen bei Bedarf, Sicherheitsfixes innerhalb von 7 Tagen" — prüfbar und haltbar.
- **F-02:** Soll die Vergleichstabelle im Repo oder auf der Website stehen? Vorschlag: im Repo (versioniert, datiert), Website verweist darauf.
