# Plan-0009: Externe Checks & Status-Seite (GAP-2)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0009](../prd/0009-externe-checks-und-statusseite.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Whiskers sieht nur von innen. Ein Container kann laufen, gesund melden und für Nutzer trotzdem nicht erreichbar sein. Der Mehrwert gegenüber einem zweiten Werkzeug daneben liegt darin, Außen- und Innenbefund in **einer** Meldung zusammenzuführen — das ist der Teil, der heute von Hand passiert.

Zwei Fallen prägen den Plan: Prüfungen können selbst zur Last werden (der Vorfall vom 26.08. in neuer Kleidung), und eine Status-Seite, die mit dem überwachten Dienst fällt, ist wertlos.

## Ziele

- Erkennung von außen, unabhängig vom gemeldeten Container-Zustand.
- Eine Meldung, die beide Sichten enthält.
- Eine Status-Seite, die den Ausfall überlebt — oder deren Grenze ehrlich dokumentiert ist.

## Arbeitspakete

### WP1: Prüf-Engine

**Zweck:** Die Außensicht.
**Schätzung:** M (3 Tage).

1. **WP1.1:** Prüfarten HTTP(S) mit Statuscode- und Inhaltsprüfung, TCP-Port, ICMP-Ping, DNS-Auflösung.
2. **WP1.2:** Je Prüfung Intervall (Untergrenze 30 s), Zeitüberschreitung, Fehlversuchsschwelle, Erwartungswert.
3. **WP1.3:** Ausführung unter dem Budget aus SP-1, mit harter Obergrenze für gleichzeitig laufende Prüfungen.
4. **WP1.4:** Ergebnisse als Zeitreihe speichern, in die vorhandene Aufbewahrung eingehängt.
5. **WP1.5:** **Warnung bei internem Prüfziel:** Ist die Zieladresse eine interne IP oder `localhost`, wird die Prüfung markiert — sie prüft dann den kurzen Weg, nicht den der Nutzer.

**Abnahme:** Ein absichtlich gestoppter Reverse-Proxy erzeugt binnen zwei Intervallen eine Meldung, während der Container-Zustand „läuft" bleibt.

### WP2: TLS-Überwachung

**Zweck:** Der häufigste stille Ausfall.
**Schätzung:** S (1 Tag).

1. **WP2.1:** Zertifikatskette und Restlaufzeit bei jeder HTTPS-Prüfung erfassen.
2. **WP2.2:** Vorwarnung ab 21 Tagen, Eskalation ab 7 Tagen.
3. **WP2.3:** Restlaufzeit als Dauerkennzahl über alle Prüfungen — nicht nur als Ereignis.

**Abnahme:** Testzertifikat mit 20 Tagen Restlaufzeit erzeugt eine Warnung.

### WP3: Außen und Innen verbinden

**Zweck:** Der eigentliche Mehrwert gegenüber zwei getrennten Werkzeugen.
**Schätzung:** M (2 Tage).

1. **WP3.1:** Optionale Zuordnung einer Prüfung zu einem Container/Workload.
2. **WP3.2:** Bei Alarm den Innenzustand zum selben Zeitpunkt beilegen: Container-Zustand, letzte Fehlerzeilen, Host-Last.
3. **WP3.3:** Gemeinsame Meldung mit beiden Sichten; Verknüpfung mit dem Incident-Objekt (attackResponse AR-1), sobald vorhanden.

**Abnahme:** Die Meldung im Abnahmefall aus WP1 enthält sowohl „HTTP 502" als auch den Container- und Datenbankzustand.

### WP4: Status-Seite

**Zweck:** Die Nutzer informieren, ohne Rückfragen.
**Schätzung:** M (3 Tage).

1. **WP4.1:** Öffentliche Seite ohne Anmeldung, nur mit **ausdrücklich freigegebenen** Prüfungen.
2. **WP4.2:** Verfügbarkeit über 7/30/90 Tage, aktuelle Störungen, Anzeigenamen statt interner Bezeichner.
3. **WP4.3:** Ein-/ausschaltbar; Standard ist aus.
4. **WP4.4:** **Überlebensprüfung:** Reverse-Proxy stoppen und feststellen, ob die Seite noch antwortet. Fällt sie mit, wird die Einschränkung in der Oberfläche benannt — keine vorgetäuschte Sicherheit.
5. **WP4.5:** Prüfen, dass weder Quelltext noch API-Antworten interne Namen enthalten.

**Abnahme:** Eine nicht freigegebene Prüfung ist auf der Seite nachweislich nicht vorhanden — auch nicht in den API-Antworten.

### WP5: Wartungsfenster

**Zweck:** Geplante Arbeiten erzeugen keine Alarme, aber auch kein Verschweigen.
**Schätzung:** S (1 Tag).

1. **WP5.1:** Zeiträume je Prüfung oder Server, Alarme unterdrückt.
2. **WP5.2:** Auf der Status-Seite als angekündigte Wartung sichtbar — Unterdrückung ohne Ankündigung ist Verschweigen.

**Abnahme:** Während eines Fensters keine Meldung, aber ein Eintrag auf der Seite.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `list_checks`, `get_check_status`, `run_check_now` — Prüfungen lesen und ad hoc auslösen. Stufe: read / write, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Der Agent führt Außen- und Innenbefund ohne Zwischenschritt zusammen. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP1 ──> WP2
  └───> WP3
  └───> WP4 ──> WP5
```

- **Extern blockiert von:** SP-1 (Budget). Ohne Budget wird dieses Paket zur nächsten Lastquelle.
- **Teilt Mechanik mit:** SP-4 (Dauer-/Schwellenlogik) und GAP-3 (die Erreichbarkeitsprüfung nach einem Deploy nutzt WP1).

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Erkennungszeit | manuelle Störung | ≤ 2 Intervalle |
| Fehlalarme | Meldungen ohne echten Ausfall je Woche | ≤ 1 |
| Trefferquote je Prüfung | Stichprobe | ≥ 50 %, sonst Prüfung entschärfen oder entfernen |
| Eigenlast | `whiskers_self_`-Aufrufrate | planbar, nicht überproportional |
| Status-Seite im Ausfall | Gegenprobe WP4.4 | Seite lädt oder Grenze ist dokumentiert |
| Interne Namen | Quelltext und API-Antworten | keine |
| Zertifikats-Restlaufzeiten | Kennzahl WP2.3 | keine unter 7 Tagen unbemerkt |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Status-Seite fällt mit dem Dienst | genau dann weg, wenn sie gebraucht wird | WP4.4 als verpflichtende Gegenprobe, Einschränkung sonst benennen |
| Prüfung sieht die Störung nicht | grün, während draußen nichts geht | WP1.5 markiert interne Ziele |
| Flatternde Prüfungen | werden nach einem Monat ignoriert | Trefferquote je Prüfung als Kennzahl |
| Datenpreisgabe | öffentliche Aufklärungshilfe | WP4.5 prüft auch die API-Antworten, nicht nur die Darstellung |
| Prüfungen als Lastquelle | der Vorfall in neuer Form | Intervall-Untergrenze, Budget, Obergrenze für gleichzeitige Prüfungen |

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M1 | WP1 | gestoppter Proxy erzeugt Meldung, Container meldet weiter „läuft" |
| M2 | WP2 | Testzertifikat löst Warnung aus |
| M3 | WP3 | eine Meldung, zwei Sichten |
| M4 | WP4 | Überlebensprüfung durchgeführt und Ergebnis dokumentiert |
| M5 | WP5 + Feldlauf | 14 Tage, Fehlalarme ≤ 1 je Woche |

## Rückweg

Prüfungen und Status-Seite sind einzeln abschaltbar, Standard ist aus. Erweist sich die Eigenlast als zu hoch, wird die Obergrenze für gleichzeitige Prüfungen gesenkt, bevor Prüfarten entfallen.

## Definition of Done

- [ ] WP1–WP5 umgesetzt
- [ ] Gestoppter Reverse-Proxy erzeugt binnen zwei Intervallen eine Meldung bei „gesundem" Container
- [ ] Meldung enthält Außen- **und** Innenbefund
- [ ] Überlebensprüfung der Status-Seite durchgeführt; Ergebnis dokumentiert, Einschränkung ggf. sichtbar
- [ ] Nicht freigegebene Prüfung erscheint weder auf der Seite noch in deren API
- [ ] Zertifikatswarnung bei 21 Tagen belegt
- [ ] Fehlalarme über 14 Tage ≤ 1 je Woche
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
