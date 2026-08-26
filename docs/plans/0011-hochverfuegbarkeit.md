# Plan-0011: Hochverfügbarkeit (GAP-4)

- **Status:** Entwurf
- **Datum:** 2026-08-26
- **Autor:** @LupusMalusDeviant
- **Basis-PRD:** [PRD-0011](../prd/0011-hochverfuegbarkeit.md)
- **Verantwortlich:** @LupusMalusDeviant

## Kontext

Einzelinstanz ist eine bewusste Entscheidung mit vier Gründen; zwei davon sind inzwischen entschärft (PostgreSQL vorhanden, Weg der JSON-Speicher in die Datenbank beschrieben). Bleiben Leader-Election und Blazor-Sitzungen.

Dieser Plan ist der letzte in der Roadmap, und das aus einem Grund: **Zwei Instanzen mit halbfertiger Koordination sind unzuverlässiger als eine saubere.** Er enthält deshalb ein ausdrückliches Abbruchkriterium.

Vorher ist zu prüfen, ob überhaupt HA gebraucht wird oder nur ein Update ohne Beobachtungslücke — WP1 plus WP5 liefern das zu einem Bruchteil der Kosten.

## Ziele

- Kein Loop läuft doppelt.
- Ein Update erzeugt keine Beobachtungslücke.
- Split-Brain ist ausgeschlossen und dauerhaft überwacht.

## Arbeitspakete

### WP0: Vorentscheidung

**Zweck:** Den teuren Teil nur bauen, wenn er gebraucht wird.
**Schätzung:** S (0,5 Tage). **Zuerst.**

1. **WP0.1:** Klären, ob das Ziel echte HA ist oder ein unterbrechungsfreies Update.
2. **WP0.2:** Bei „unterbrechungsfreies Update": nur WP1 und WP5 umsetzen, den Rest verwerfen. Das ist ausdrücklich ein gültiges Ergebnis dieses Plans.

**Ergebnis:** Eine bewusste Entscheidung statt einer stillschweigenden Annahme.

### WP1: Leader-Election

**Zweck:** Die Grundlage von allem.
**Schätzung:** M (3 Tage).

1. **WP1.1:** Pachtzeile in der Datenbank: Instanzkennung, Ablaufzeitpunkt, Erneuerung. Vorschlag 30 s Pacht, 10 s Erneuerung — gegen die tatsächliche Datenbanklatenz kalibrieren.
2. **WP1.2:** Übernahme nur nach abgelaufener Pacht, mit atomarer Bedingung in der Datenbank (kein Lesen-dann-Schreiben).
3. **WP1.3:** **Pachtverlust stellt die Loops binnen eines Erneuerungsintervalls ein** — nachweisbar, nicht angenommen.
4. **WP1.4:** Leader-Prüfung an derselben zentralen Stelle wie Pausenabfrage (Plan-0005 WP1.4) und Kennzahlen (Plan-0003 WP2.1), damit kein Loop sie vergessen kann.
5. **WP1.5:** Kennzahl `whiskers_self_is_leader` je Instanz.

**Abnahme:** Datenbankverbindung des Leaders unterbrechen ⇒ er stellt die Loops ein, **bevor** eine andere Instanz die Pacht übernimmt. Zeitliche Überlappung = 0.

### WP2: Zustand aus dem Prozess holen

**Zweck:** Verhindern, dass ein Leaderwechsel Sprünge erzeugt.
**Schätzung:** L (4–6 Tage, abhängig von `changeme.md` C7).

1. **WP2.1:** JSON-Dateispeicher in die Datenbank (C7) oder ausdrücklich als Leader-only markieren.
2. **WP2.2:** In-Memory-Zustände inventarisieren: Cooldowns (LogMonitor), Wasserzeichen, Health-Zustände, Circuit-Zustand, Fehlschlagzähler.
3. **WP2.3:** Je Zustand entscheiden: teilen (Datenbank), verwerfen (nach Wechsel neu aufbauen) oder Leader-only. **Verwerfen ist zulässig**, solange der Wechsel keine falschen Alarme erzeugt.
4. **WP2.4:** Kennzahl: Alarmrate in den ersten fünf Minuten nach einem Leaderwechsel gegen den Normalwert.

**Abnahme:** Ein Leaderwechsel erzeugt keine erneuten Alarme für längst bekannte Zustände.

### WP3: Sitzungen und Chart

**Zweck:** Der Betriebsteil.
**Schätzung:** M (3 Tage).

1. **WP3.1:** Klebende Sitzungen plus sauberes Wiederverbinden; im Zweifel Neuanmeldung statt stiller Fehlfunktion.
2. **WP3.2:** Helm-Chart: `replicas > 1`, `RollingUpdate`, PodDisruptionBudget, angepasste Proben.
3. **WP3.3:** PostgreSQL als harte Voraussetzung — Start mit SQLite und `replicas > 1` **verweigern** mit verständlicher Meldung.
4. **WP3.4:** Selbstmetriken je Instanz getrennt, mit Instanzkennung (Plan-0003).

**Abnahme:** Start mit SQLite und zwei Replikaten scheitert verständlich, statt Daten zu beschädigen.

### WP4: Split-Brain-Überwachung

**Zweck:** Die einzige wirklich gefährliche Fehlerart dauerhaft im Blick.
**Schätzung:** S (1 Tag).

1. **WP4.1:** Summe von `whiskers_self_is_leader` über alle Instanzen als Dauerkennzahl.
2. **WP4.2:** Summe ≠ 1 ⇒ Meldung höchster Stufe. **Summe 0 ist genauso zu behandeln wie Summe 2** — im ersten Fall läuft nichts, und alles sieht ruhig aus.
3. **WP4.3:** Architekturtest: schlägt fehl, sobald ein `BackgroundService` ohne Leader-Prüfung existiert.

**Abnahme:** Künstlich erzwungenes Doppel-Leadertum wird binnen eines Erneuerungsintervalls gemeldet.

### WP5: Update ohne Lücke

**Zweck:** Der eigentliche Nutzen für die Zielgruppe.
**Schätzung:** S (1 Tag).

1. **WP5.1:** Übernahme der Leaderschaft erst nach bestandener Bereitschaftsprüfung der neuen Instanz.
2. **WP5.2:** Abgabe der Pacht beim geordneten Herunterfahren, statt auf den Ablauf zu warten.
3. **WP5.3:** Messung der Lücke über `last_success_timestamp` je Loop während eines Rolling Updates.

**Abnahme:** Lücke kleiner als ein Zyklusintervall.

### WP-MCP: Agenten-Oberfläche

**Zweck:** Das Paket ist erst fertig, wenn der Agent es benutzen kann — siehe [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md).
**Schätzung:** S (0,5–1 Tag).

1. **WP-MCP.1:** Werkzeuge: `get_cluster_role` — Leaderschaft, Instanzkennung, Pachtalter. Stufe: read, per Attribut am Werkzeug deklariert (Plan-0013 WP1).
2. **WP-MCP.2:** Modul-Eintrag in `McpToolTypes` ergänzen und die Katalog-Momentaufnahme fortschreiben (Plan-0013 WP3).
3. **WP-MCP.3:** Beschreibung nennt Zweck, Wirkung und Nebenwirkung in einem Satz; schreibende Werkzeuge werden in die Wirkungskontrolle (SP-6) eingehängt.
4. **WP-MCP.4:** CHANGELOG-Eintrag mit dem Hinweis, dass der Konnektor neu verbunden werden muss.

**Abnahme:** Der Agent erkennt eine Leader-Summe von 0 oder 2 als Störung. Gegenprobe am **laufenden** Server: `tools/list` enthält die Werkzeuge mit der erwarteten Stufe.

## Reihenfolge und Abhängigkeiten

```
WP0 ──> WP1 ──> WP2 ──> WP3 ──> WP5
          └───> WP4 (begleitend ab WP1)
extern: stableDB ✅, changeme C7 ──> WP2, SP-3 ──> WP1.5/WP4
```

- **Extern blockiert von:** `changeme.md` C7, SP-3.
- Letzte Welle der Roadmap. Kein anderes Paket wartet darauf.

## Prüf- und Messstellen im Betrieb

| Messstelle | Quelle | Erwartung |
|---|---|---|
| Leader-Summe | `whiskers_self_is_leader` über alle Instanzen | konstant 1 |
| Doppelte Alarme | Alarm-Historie auf Duplikate | keine |
| Lücke beim Update | `last_success_timestamp` je Loop | < 1 Intervall |
| Alarmrate nach Leaderwechsel | Kennzahl WP2.4 | wie im Normalbetrieb |
| Datenbanklast der Pacht | Abfragerate | vernachlässigbar |
| Sitzungsverhalten | Instanz während Nutzung beenden | Wiederverbindung oder klare Neuanmeldung |

## Risiken und Gegenmaßnahmen

| Risiko | Wirkung | Gegenmaßnahme |
|---|---|---|
| Split-Brain | doppelte Aktionen — zwei Neustarts, zwei Updates, zwei Drosselungen | WP1.2 atomare Übernahme, WP4 Dauerüberwachung |
| Leader-Summe 0 | nichts läuft, alles wirkt ruhig | WP4.2 behandelt beide Abweichungen gleich |
| Ein Loop vergisst die Prüfung | seltene Doppelausführung, schwer zu finden | WP1.4 zentrale Stelle + WP4.3 Architekturtest |
| Zustand driftet | Sprünge und Fehlalarme beim Wechsel | WP2.2/WP2.3 Inventar, WP2.4 Kennzahl |
| Verfügbarkeit gewonnen, Sicherheit verloren | schlechter als vorher | **Abbruchkriterium** siehe unten |

## Abbruchkriterium

Bleibt die Leader-Summe über **eine Woche Dauerbetrieb** nicht konstant 1, wird das Vorhaben zurückgestellt und der Einzelinstanzbetrieb bleibt der unterstützte Weg. Nachbessern an einer instabilen Koordination ist ausdrücklich nicht der vorgesehene Umgang — eine saubere Einzelinstanz ist besser als zwei unsichere.

## Meilensteine

| M | Inhalt | Nachweis |
|---|---|---|
| M0 | WP0 | Entscheidung dokumentiert; ggf. Plan auf WP1+WP5 gekürzt |
| M1 | WP1 + WP4 | Verbindungsabriss: Überlappung 0; Doppel-Leadertum wird gemeldet |
| M2 | WP2 | Leaderwechsel ohne erneute Alarme |
| M3 | WP3 | SQLite mit zwei Replikaten scheitert verständlich |
| M4 | WP5 | Update-Lücke < 1 Intervall, gemessen |
| M5 | Dauerbetrieb | eine Woche mit zwei Instanzen, Leader-Summe konstant 1 |

## Rückweg

`replicas: 1` bleibt jederzeit der unterstützte Standardbetrieb. Die Leader-Election ist auch dort aktiv (eine Instanz, dauerhaft Leader) und damit kein Sonderpfad — das hält den Code einfach und den Rückweg trivial.

## Definition of Done

- [ ] WP0 entschieden und dokumentiert
- [ ] WP1–WP5 umgesetzt (bzw. WP1+WP5, falls WP0 so entschieden hat)
- [ ] Verbindungsabriss des Leaders: zeitliche Überlappung zweier Leader = 0, gemessen
- [ ] 24-Stunden-Lauf mit zwei Instanzen ohne doppelte Alarme oder Aufgaben
- [ ] Rolling Update mit Lücke < 1 Zyklusintervall
- [ ] Start mit SQLite und `replicas > 1` schlägt verständlich fehl
- [ ] Architekturtest verhindert Loops ohne Leader-Prüfung
- [ ] Eine Woche Dauerbetrieb mit Leader-Summe konstant 1 — sonst greift das Abbruchkriterium
- [ ] MCP-Werkzeuge ausgeliefert, im Katalog eingetragen und am laufenden Server in `tools/list` sichtbar
