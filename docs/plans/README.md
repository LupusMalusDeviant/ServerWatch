# docs/plans — Implementierungspläne

Ein Plan je PRD: **Arbeitspakete, Reihenfolge, Abhängigkeiten, Meilensteine, Rückweg**. Das Was und Warum steht im zugehörigen PRD unter [../prd/](../prd/).

Jeder Plan trägt neben den Arbeitspaketen:

- **Abnahme je Arbeitspaket** — was genau gemessen wird, nicht „läuft".
- **Prüf- und Messstellen im Betrieb** — Befehl oder Kennzahl mit Erwartungswert.
- **Risiken und Gegenmaßnahmen** — mit der Messstelle, an der das Risiko sichtbar würde.
- **Meilensteine** mit Nachweis, **Rückweg** und eine abhakbare **Definition of Done**.

| Plan | Paket | PRD |
|---|---|---|
| [0001](0001-abbruch-und-lastbudget.md) | Abbruch & Lastbudget | [PRD-0001](../prd/0001-abbruch-und-lastbudget.md) |
| [0002](0002-fensterdeckel-und-aussperrung.md) | Fensterdeckel & Aussperrung | [PRD-0002](../prd/0002-fensterdeckel-und-aussperrung.md) |
| [0003](0003-selbstbeobachtung.md) | Selbstbeobachtung | [PRD-0003](../prd/0003-selbstbeobachtung.md) |
| [0004](0004-host-und-baseline-alarme.md) | Host- & Baseline-Alarme | [PRD-0004](../prd/0004-host-und-baseline-alarme.md) |
| [0005](0005-not-aus.md) | Not-Aus | [PRD-0005](../prd/0005-not-aus.md) |
| [0006](0006-wirkungskontrolle.md) | Wirkungskontrolle | [PRD-0006](../prd/0006-wirkungskontrolle.md) |
| [0007](0007-hygiene-inventar.md) | Hygiene-Inventar | [PRD-0007](../prd/0007-hygiene-inventar.md) |
| [0008](0008-kubernetes-paritaet.md) | Kubernetes-Parität | [PRD-0008](../prd/0008-kubernetes-paritaet.md) |
| [0009](0009-externe-checks-und-statusseite.md) | Externe Checks & Status-Seite | [PRD-0009](../prd/0009-externe-checks-und-statusseite.md) |
| [0010](0010-git-deploy-ausbau.md) | Git-Deploy-Ausbau | [PRD-0010](../prd/0010-git-deploy-ausbau.md) |
| [0011](0011-hochverfuegbarkeit.md) | Hochverfügbarkeit | [PRD-0011](../prd/0011-hochverfuegbarkeit.md) |
| [0012](0012-reife-und-vertrauen.md) | Reife & Vertrauen | [PRD-0012](../prd/0012-reife-und-vertrauen.md) |
| [0013](0013-mcp-und-agentenoberflaeche.md) | **MCP- und Agenten-Oberfläche (Querschnitt)** — WP1–WP3 laufen **vor** allen anderen Paketen | [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) |

Jeder Plan 0001–0012 trägt zusätzlich ein `WP-MCP`-Arbeitspaket: das Paket ist erst fertig, wenn der Agent es benutzen kann.

## Drei Pläne beginnen mit einem WP0

Kein Zufall — es sind die drei, bei denen ohne Vorarbeit kein belastbarer Nachweis möglich wäre:

- **[Plan-0004](0004-host-und-baseline-alarme.md) WP0:** die Metriken des Vorfalls sichern. Sie sind der Prüfstand; eine Regel, die den bekannten Vorfall an echten Daten nicht findet, ist unbrauchbar.
- **[Plan-0005](0005-not-aus.md) WP0:** die nicht abschaltbare Aufsichtsregel, bevor der Not-Aus existiert. Sonst schafft der Schalter genau die Blindheit, die der Vorfall so teuer gemacht hat.
- **[Plan-0008](0008-kubernetes-paritaet.md) WP0:** echte Cluster. Dieses Paket lässt sich vollständig ohne Cluster entwickeln und sieht dabei plausibel aus — der heutige Provider ist der Beweis.

## Wellenplanung

Siehe [../roadmap/hardeningAndParity.md](../roadmap/hardeningAndParity.md) §5.
