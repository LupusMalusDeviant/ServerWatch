# docs/prd — Product Requirements Documents

Ein PRD je Paket: **Problem, Ziele, Anforderungen, Abnahmekriterien**. Kein Code, keine Architekturentscheidung (dafür [../adr/](../adr/)), keine Umsetzungsschritte (dafür [../plans/](../plans/)).

Jeder PRD hier trägt zwei Abschnitte, die über das Standardformat hinausgehen und in diesem Projekt verbindlich sind:

- **Prüf- und Messstellen** — wo im laufenden Betrieb gemessen wird, mit Grün- und Rotwerten.
- **Woran ich sehe, dass es bricht** — die Versagensarten der Lösung selbst. Ein grüner Testlauf beweist, dass der bedachte Fall funktioniert; gebraucht wird die Messstelle, die anschlägt, wenn er es nicht mehr tut.

Dazu **Do's** und **Don'ts** je Paket sowie die Abhängigkeiten.

## Selbstschutz (SP)

Auslöser: [Vorfall 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) — Whiskers hat einen überwachten Server sechs Tage lang lahmgelegt.

| PRD | Paket | Plan |
|---|---|---|
| [0001](0001-abbruch-und-lastbudget.md) | Abbruch & Lastbudget — echter Aufrufabbruch, geteiltes Budget je Server, Circuit Breaker | [Plan-0001](../plans/0001-abbruch-und-lastbudget.md) |
| [0002](0002-fensterdeckel-und-aussperrung.md) | Fensterdeckel & Aussperrung — die Wasserzeichen-Ratsche entschärfen, unlesbare Container melden | [Plan-0002](../plans/0002-fensterdeckel-und-aussperrung.md) |
| [0003](0003-selbstbeobachtung.md) | Selbstbeobachtung — `whiskers_self_*`, Aktions-Zeitachse | [Plan-0003](../plans/0003-selbstbeobachtung.md) |
| [0004](0004-host-und-baseline-alarme.md) | Host- & Baseline-Alarme — die Lücke, durch die der Vorfall fiel | [Plan-0004](../plans/0004-host-und-baseline-alarme.md) |
| [0005](0005-not-aus.md) | Not-Aus — Loops pausieren, manuell und automatisch | [Plan-0005](../plans/0005-not-aus.md) |
| [0006](0006-wirkungskontrolle.md) | Wirkungskontrolle — jede automatische Aktion wird an ihrer Wirkung gemessen | [Plan-0006](../plans/0006-wirkungskontrolle.md) |
| [0007](0007-hygiene-inventar.md) | Hygiene-Inventar — Log-Rotation und Selbstausschluss der Zugriffspfad-Container | [Plan-0007](../plans/0007-hygiene-inventar.md) |

## Wettbewerbsparität (GAP)

| PRD | Paket | Plan |
|---|---|---|
| [0008](0008-kubernetes-paritaet.md) | Kubernetes-Parität — Alarme, Metriken, CVE, MCP auch auf K8s | [Plan-0008](../plans/0008-kubernetes-paritaet.md) |
| [0009](0009-externe-checks-und-statusseite.md) | Externe Checks & Status-Seite — die fehlende Außensicht | [Plan-0009](../plans/0009-externe-checks-und-statusseite.md) |
| [0010](0010-git-deploy-ausbau.md) | Git-Deploy-Ausbau — von „läuft" zu „erreichbar", mit Rückweg | [Plan-0010](../plans/0010-git-deploy-ausbau.md) |
| [0011](0011-hochverfuegbarkeit.md) | Hochverfügbarkeit — Leader-Election, Update ohne Beobachtungslücke | [Plan-0011](../plans/0011-hochverfuegbarkeit.md) |
| [0012](0012-reife-und-vertrauen.md) | Reife & Vertrauen — Positionierung, Betriebsbelege, Umgang mit dem Vorfall | [Plan-0012](../plans/0012-reife-und-vertrauen.md) |

## Querschnitt

| PRD | Paket | Plan |
|---|---|---|
| [0013](0013-mcp-und-agentenoberflaeche.md) | MCP- und Agenten-Oberfläche — gilt für **alle** Pakete oben und für die AR-Pakete in [attackResponse.md](../roadmap/attackResponse.md) | [Plan-0013](../plans/0013-mcp-und-agentenoberflaeche.md) |

Jedes Paket-PRD trägt dafür eine `FR-MCP`-Zeile, jeder Plan ein `WP-MCP`-Arbeitspaket. Ein Paket ohne Werkzeug gilt als nicht fertig — die Aussage „regierte Autonomie" hält nur, solange der Agent sieht und auslösen kann, was die Oberfläche zeigt.

## Reihenfolge

Verbindlich ist die Wellenplanung in [../roadmap/hardeningAndParity.md](../roadmap/hardeningAndParity.md) §5. Zwei Regeln daraus:

- **Plan-0013 WP1–WP3 vor allen Paketen** — sie legen die Form fest, in der Werkzeuge entstehen. Ein halber Tag Vorarbeit statt zwölf späterer Umbauten.
- **SP-1 zuerst unter den Paketen, ohne Ausnahme** — ohne echten Aufrufabbruch ist jede Drosselung wirkungslos.
