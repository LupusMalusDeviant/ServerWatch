# hardeningAndParity.md — Selbstschutz und Wettbewerbsparität

> **Ziel:** Zwei Stränge, die aus derselben Analyse stammen und deshalb in einem Dokument stehen:
> - **Strang SP (Selbstschutz):** Whiskers darf die Server, die es überwacht, nicht beschädigen — und muss merken, wenn es doch passiert. Auslöser ist [der Vorfall vom 26.08.2026](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md).
> - **Strang GAP (Parität):** Die Punkte, an denen Whiskers im direkten Vergleich mit Portainer, Coolify, Netdata, Uptime Kuma und den K8s-Werkzeugen heute verliert.
>
> **Gemeinsamer Nenner beider Stränge und der Grund, warum sie zusammengehören:** Whiskers misst viel und bewertet fast nichts. Der Vorfall wurde 1.600-mal am Tag gemessen und nie gemeldet; das Angriffsszenario in [attackResponse.md](attackResponse.md) scheitert an derselben unterbrochenen Kette. Der Satz aus dem Vorfallsbericht ist die kürzeste Fassung: *alle vorhandenen Schutzmaßnahmen schützen Whiskers vor dem Server, keine schützt den Server vor Whiskers.*
>
> **Aufwand:** SP ≈ 12–18 Arbeitstage, GAP ≈ 25–40 (GAP-1 und GAP-4 dominieren). **Risiko:** SP-1 fasst die Docker-Aufrufkette an, die jeder Loop benutzt — das ist der invasivste Eingriff im ganzen Dokument.

---

## 1. Warum das vor dem Marketing kommt

`beatPortainerCoolify.md` Phase 0 nennt i18n und Screenshots als Launch-Blocker. Das stimmt nicht mehr. Seit dem 26.08. existiert ein schriftlich dokumentierter Fall, in dem Whiskers einen 2-Kern-Server sechs Tage lang auf 98 % CPU gefahren hat. Das ist genau die Geschichte, die eine Adoption in `r/selfhosted` beendet — und sie ist wahr und öffentlich nachlesbar, sobald das Repo Aufmerksamkeit bekommt.

**SP-1 und SP-2 sind damit Launch-Voraussetzung, nicht Feinschliff.** Sie gehören in `beatPortainerCoolify.md` Phase 0 vor L1/L2.

Der Umkehrschluss ist die Chance: „Das Werkzeug, das seine eigene Last budgetiert und meldet, wenn es dem überwachten Server schadet" ist eine Aussage, die kein Wettbewerber trifft. Der Vorfall ehrlich behandelt ist ein Vertrauensgewinn, verschwiegen ein Totalschaden.

---

## 2. Strang SP — Selbstschutz

| # | Paket | Kern | PRD | Plan |
|---|---|---|---|---|
| SP-1 | Abbruch & Lastbudget | `CancellationToken` durch die Docker-Kette; ein geteiltes Lastbudget je Server für **alle** Loops; Circuit Breaker; Lastinvarianten in CI | [PRD-0001](../prd/0001-abbruch-und-lastbudget.md) | [Plan-0001](../plans/0001-abbruch-und-lastbudget.md) |
| SP-2 | Fensterdeckel & Aussperrung | Wasserzeichen-Ratsche entschärfen; Container nach n Fehlschlägen mit Backoff aussperren — **und das melden** | [PRD-0002](../prd/0002-fensterdeckel-und-aussperrung.md) | [Plan-0002](../plans/0002-fensterdeckel-und-aussperrung.md) |
| SP-3 | Selbstbeobachtung | `self:`-Metriken (Call-Rate, Latenz, in-flight, Zyklusdauer, Timeouts), auf `/metrics` exportiert; Aktions-Zeitachse über den Metriken | [PRD-0003](../prd/0003-selbstbeobachtung.md) | [Plan-0003](../plans/0003-selbstbeobachtung.md) |
| SP-4 | Host- & Baseline-Alarme | Host-CPU/RAM analog `disk:{server}`; „Last, die kein Container erklärt"; rollende Baseline statt fester Schwelle | [PRD-0004](../prd/0004-host-und-baseline-alarme.md) | [Plan-0004](../plans/0004-host-und-baseline-alarme.md) |
| SP-5 | Not-Aus | Hintergrund-Loops pausieren — manuell je Server/global, automatisch bei offenem Circuit | [PRD-0005](../prd/0005-not-aus.md) | [Plan-0005](../plans/0005-not-aus.md) |
| SP-6 | Wirkungskontrolle | Jede automatische Aktion wird auf Wirkung geprüft und bei Ausbleiben zurückgenommen | [PRD-0006](../prd/0006-wirkungskontrolle.md) | [Plan-0006](../plans/0006-wirkungskontrolle.md) |
| SP-7 | Hygiene-Inventar | Container ohne Log-Rotation melden, **bevor** sie zünden; Docker-Proxy-Container aus dem Log-Scan nehmen | [PRD-0007](../prd/0007-hygiene-inventar.md) | [Plan-0007](../plans/0007-hygiene-inventar.md) |

### Abhängigkeiten SP

```
SP-1 (Abbruch + Budget) ──┬──> SP-2 (Deckel + Aussperrung)
                          ├──> SP-5 (Not-Aus nutzt denselben Circuit)
                          └──> SP-3 (Budget-Zähler SIND die self:-Metriken)
SP-3 ──> SP-4 (Baseline braucht die Zeitreihen)
SP-4 ──> SP-6 (Wirkungskontrolle misst gegen dieselben Signale)
SP-7  unabhängig, jederzeit
attackResponse AR-1 (Incident-Objekt) ──> SP-2/SP-4/SP-5 melden dort hinein
```

> **Ohne SP-1 ist jede Drosselung wirkungslos.** Der Vorfallsbericht sagt es unmissverständlich: solange ein abgelaufener Aufruf serverseitig weiterläuft, entlastet keine Pause und kein Backoff den Server. SP-1 zuerst, ohne Ausnahme.

---

## 3. Strang GAP — Wettbewerbsparität

| # | Paket | Verlorener Vergleichspunkt | PRD | Plan |
|---|---|---|---|---|
| GAP-1 | Kubernetes-Parität | Auf einem K8s-Server bleibt heute ein Pod-Viewer mit Start/Stop übrig — Alarme, Metriken, CVE, MCP: alles Docker-only | [PRD-0008](../prd/0008-kubernetes-paritaet.md) | [Plan-0008](../plans/0008-kubernetes-paritaet.md) |
| GAP-2 | Externe Checks & Status-Seite | Uptime Kuma: 90+ Kanäle, synthetische Checks, Status-Seiten. Whiskers: 9 Kanäle, nur Innensicht | [PRD-0009](../prd/0009-externe-checks-und-statusseite.md) | [Plan-0009](../plans/0009-externe-checks-und-statusseite.md) |
| GAP-3 | Git-Deploy-Ausbau | Coolify: 280+ Ein-Klick-Dienste, Domains/TLS integriert. Whiskers F5: https-only, v1 | [PRD-0010](../prd/0010-git-deploy-ausbau.md) | [Plan-0010](../plans/0010-git-deploy-ausbau.md) |
| GAP-4 | Hochverfügbarkeit | Single-Replica by design; Blazor-Circuits, 8+ Loops ohne Leader-Election, JSON-Stores mit Prozess-Cache | [PRD-0011](../prd/0011-hochverfuegbarkeit.md) | [Plan-0011](../plans/0011-hochverfuegbarkeit.md) |
| GAP-5 | Reife & Vertrauen | Ein Entwickler, v0.13.x, gegen 10 Jahre Portainer und 50.000 Coolify-Sterne | [PRD-0012](../prd/0012-reife-und-vertrauen.md) | [Plan-0012](../plans/0012-reife-und-vertrauen.md) |

### Abhängigkeiten GAP

```
SP-1..SP-4  ──> GAP-1 (K8s erbt die Loops — erst reparieren, dann portieren)
SP-3        ──> GAP-4 (ohne Selbstbeobachtung ist Multi-Replica nicht debuggbar)
stableDB ✅ + changeme C7 (Stores→DB) ──> GAP-4
GAP-2  unabhängig
GAP-3  unabhängig
GAP-5  läuft durchgehend nebenher
```

> **Die Reihenfolge SP vor GAP-1 ist keine Vorliebe.** Wer die Loops zuerst auf Kubernetes portiert, portiert die Wasserzeichen-Ratsche und das fehlende Lastbudget gleich mit — auf eine API, die noch weniger verzeiht als dockerd.

---

## 3a. Querschnitt MCP — jedes Paket bringt seine Agenten-Oberfläche mit

| # | Paket | Kern | PRD | Plan |
|---|---|---|---|---|
| MCP | MCP- und Agenten-Oberfläche | Stufe am Werkzeug statt in einer Liste nebenan; Registrierungstest je Modul; Katalog-Momentaufnahme; die sieben stummen Module nachrüsten | [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) | [Plan-0013](../plans/0013-mcp-und-agentenoberflaeche.md) |

Die Positionierung von Whiskers ist **regierte Autonomie** — der Agent lebt im Produkt, nicht als Adapter davor. Diese Aussage hält nur, solange die MCP-Oberfläche mitwächst. Jedes Paket, das neue Signale erzeugt oder neue Eingriffe ermöglicht, ohne sie über MCP verfügbar zu machen, macht den Agenten für genau den Bereich blind, den es gerade gebaut hat.

Der Ist-Stand zeigt, dass das kein theoretisches Risiko ist:

- **Sieben Module liefern heute null Werkzeuge** (`GitDeploy`, `Deployment`, `HostManagement`, `ImageUpdate`, `VolumeBackups`, `Notifications`, `Terminal`). Der Agent kann weder deployen noch Images aktualisieren noch Sicherungen anstoßen.
- **Ein Werkzeug ohne Eintrag in `DefaultToolLevels` wird stillschweigend admin-only** (`McpPermissionCheck.cs:31` fällt auf `admin` zurück) — registriert, in `tools/list` sichtbar, für den Agenten mit `write`-Obergrenze aber **immer** abgelehnt.
- **Der Registrierungstest prüft nur eine Untergrenze** (`count > 40`). Fällt ein Modul aus dem Katalog, bleibt er grün — nachdem genau diese Fehlerklasse den ausgelieferten MCP-Server von 0.12.0 bis 0.13.0 auf **null** Werkzeuge gesetzt hat, über mehrere Releases unbemerkt.

Das ist dasselbe Muster wie im Vorfall vom 26.08.: geliefert, registriert, nie bewertet.

> **Plan-0013 WP1–WP3 laufen vor allen anderen Paketen.** Sie legen die Form fest, in der Werkzeuge entstehen. Danach entsteht jedes Paketwerkzeug direkt richtig; umgekehrt müssten zwölf Pakete nachträglich umgebaut werden.

Jedes Paket-PRD trägt dafür eine `FR-MCP`-Zeile, jeder Plan ein `WP-MCP`-Arbeitspaket und eine DoD-Zeile. Der Sollzustand je Paket steht in [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) §„Werkzeuge je Paket".

---

## 4. Nicht verhandelbare Regeln für beide Stränge

**Jede Selbstdrosselung wird gemeldet.** Der Satz stammt aus dem Vorfallsbericht und gilt für jedes Paket in Strang SP. Eine stille Schutzmaßnahme verwandelt „leise" in „blind" — und versteckt den nächsten Vorfall hinter der Maßnahme gegen den letzten.

**Kein Signal ohne Gegenprobe.** Jedes neue Alarm-Signal braucht einen dokumentierten Weg, es absichtlich auszulösen. Ein Alarm, dessen Auslösung nie beobachtet wurde, ist eine Vermutung.

**Kein Paket gilt als fertig, weil es läuft.** Jeder PRD und jeder Plan in diesem Strang trägt einen Abschnitt *„Woran ich sehe, dass es bricht"*. Ein grüner Testlauf beweist, dass der Fall funktioniert, den jemand bedacht hat. Gebraucht wird die Messstelle, die im Betrieb anschlägt, wenn genau das nicht mehr stimmt.

**Nichts an fremden Systemen automatisch verändern.** Stufe 0 (sich selbst zurücknehmen) läuft ohne Rückfrage. Alles, was den überwachten Server verändert, ist Vorschlag oder Freigabe. Begründung im Vorfallsbericht: der Log-Monitor hat den Vorfall in dem Glauben verursacht, sein Timeout schütze bereits.

**Kein Paket ohne MCP-Oberfläche.** Was die Oberfläche zeigt, muss der Agent abfragen können; was sie auslöst, muss er unter Guardrails auslösen können — mit den in [PRD-0013](../prd/0013-mcp-und-agentenoberflaeche.md) benannten Ausnahmen (Zwei-Personen-Freigaben, Entsperrung nach Schutzabschaltung). Ein Paket ohne Werkzeug gilt als nicht fertig, nicht als „Werkzeug kommt später".

---

## 5. Reihenfolge über beide Stränge

| Welle | Inhalt | Warum jetzt |
|---|---|---|
| **0 — Form festlegen** | MCP (Plan-0013 WP1–WP3) | Legt fest, wie Werkzeuge entstehen. Ein halber Tag Vorarbeit, der zwölf spätere Umbauten erspart. |
| **1 — Blutung stoppen** | SP-1, SP-2, SP-7 | Behebt den dokumentierten Vorfall an der Ursache. SP-7 ist billig und nimmt den Auslöser weg. |
| **2 — Sehen lernen** | SP-3, SP-4, SP-5 | Ohne diese Welle merkt niemand den nächsten Vorfall. Danach ist Whiskers erstmals sein eigener Nutzer. |
| **3 — Vertrauen belegen** | SP-6, GAP-5, GAP-2 | Wirkungskontrolle macht Automatik verantwortbar; GAP-5/GAP-2 sind die sichtbaren Vergleichspunkte mit dem besten Aufwand-Nutzen-Verhältnis. |
| **4 — Reichweite** | GAP-1, GAP-3 | Große Pakete, die auf einer reparierten Basis stehen müssen. |
| **5 — Skalierung** | GAP-4 | Erst sinnvoll, wenn Betrieb und Beobachtbarkeit belastbar sind. |

**Kleinster sinnvoller Schnitt: Welle 1.** Danach ist der bekannte Fehler behoben. **Kleinster verantwortbarer Schnitt vor einem Launch: Welle 1 + 2.** Denn erst dann fällt ein Rückfall auf, ohne dass ein Mensch zufällig auf die Übersichtsseite schaut.

---

## 4a. Strang EH — Ehrlichkeit und Selbstnachweis (neu, 2026-08-27)

Aufgenommen nach einem Tag, an dem vier reale Probleme durch neue Wächter auffielen — und alle vier dieselbe
Form hatten: **etwas hatte still aufgehört zu funktionieren, und alles sah gesund aus.** Trivy lieferte seit
Monaten nichts, ein entfernter Server meldete weiter Befunde, ein Container trug 29 Tage alte Scandaten, ein
Log wuchs auf 1,78 GB. Keines davon war ein Fehler im Sinne von „etwas ist kaputtgegangen"; jedes war ein
Ausbleiben, das wie Ruhe aussah.

Alles auf den Strängen SP und GAP macht Whiskers **größer**. Dieser Strang macht es **ehrlicher**, und bei
einem Werkzeug, dessen Kernversprechen „ich sage dir, wenn etwas nicht stimmt" lautet, ist das die wertvollere
Richtung.

| # | Paket | Was es löst | Aufwand |
|---|---|---|---|
| EH-1 | **Kanarienvogel für die Erkennungskette** | Whiskers legt periodisch selbst einen winzigen Container an, der eine bekannte Fehlerzeile ausgibt und ein Image mit bekanntem CVE trägt, und behauptet: binnen N Minuten muss die eigene Kette das gemeldet haben. Bleibt die Meldung aus, ist die **Erkennung** kaputt, nicht der Container. Tests beweisen die Kette zum Bauzeitpunkt; das hier beweist sie jetzt — und macht aus „unbewiesen im Ruhighalten" einen Beweis von der anderen Seite: wenn der Kanarienvogel zuverlässig gefunden wird und sonst Stille herrscht, ist die Stille echt. | S |
| EH-2 | **Herkunft der Konfiguration** | Für jede wirksame Einstellung zeigen, welche Quelle gewonnen hat (Standard / appsettings / Umgebung / `data/app-settings.json`). Anlass: `CveMonitor__CheckIntervalHours=1` war im Container gesetzt, sichtbar und **wirkungslos** — `app-settings.json` gewinnt, und nichts sagte das. Übersetzt „ich habe es doch gesetzt" in eine Tabelle. | S |
| EH-3 | **Änderungsstrom der Flotte** | Container erstellt/entfernt/Image gewechselt, Konfigurationsdrift, Server hinzugekommen — mit Zeitstempel. Fast jeder Vorfall beginnt mit „was hat sich geändert?", und das ist heute die einzige Frage, die Whiskers nicht beantworten kann. Am 2026-08-27 wurde sie viermal durch Archäologie beantwortet (Container-IDs vergleichen, `ScannedAt` lesen). | M |
| EH-4 | **Abdeckung als Zahl** | Je Fähigkeit und Server: CVE 8/9, Log-Scan 8/9 (1 durch Regel X ausgenommen), Metriken 9/9 — muss 100 % sein oder sich erklären. Im Log stand bereits „8 gescannt, von 9"; die Lücke wurde gedruckt und von niemandem gezählt. Genau darin saß ghostunnel vier Wochen. Verallgemeinert den Fund, statt ihn einmalig zu reparieren. | S–M |
| EH-5 | **Wiederherstellungs-Übung** | Die letzte F3-Sicherung periodisch in einen Wegwerf-Container zurückspielen und behaupten: bootet, Migrationen sauber, N Datensätze da. Eine Sicherung, die nie zurückgespielt wurde, ist eine Behauptung. | S–M |
| EH-6 | **„Du sägst den Ast, auf dem du sitzt"-Prüfung** | Vor einer Aktion erkennen, ob das Ziel auf dem Pfad liegt, über den die Aktion selbst läuft — und dann abkoppeln statt mittendrin sterben. Am 2026-08-27 traf das zweimal zu: ghostunnel neu erstellen kappt die mTLS-Leitung, über die der Befehl kam, und ein Whiskers-Selbstdeploy killt den eigenen Container mitten im Flip. Beides ist heute Erfahrungswissen in einer Memory-Datei, kein Mechanismus. | S |

### Abhängigkeiten EH

```
SP-3 (Selbstbeobachtung) ──> EH-1 (der Kanarienvogel misst gegen dieselben Zähler)
SP-3                     ──> EH-4 (Abdeckung ist eine Selbstmetrik)
EH-3 (Änderungsstrom)    ──> GAP-6 (Update-Bewertung braucht die Historie)
EH-6  unabhängig, klein, sofort
```

---

## 4b. Strang GAP, Erweiterung (2026-08-27)

| # | Paket | Verlorener Vergleichspunkt / Nutzen | Aufwand |
|---|---|---|---|
| GAP-6 | **Update-Bewertung: bricht das etwas?** | Vor einem Image-Update ausrechnen, was sich ändert: Basis-OS-Wechsel, Major-Sprung am Tag, geänderte `VOLUME`/`EXPOSE`/`ENTRYPOINT`/`USER` im neuen Image gegen die laufende Container-Konfiguration — und wie viele CVEs das Update tatsächlich schließt. Ergebnis ist ein Satz wie „schließt 41 CVEs, wechselt den Entrypoint, verlangt ein neues Volume — mittleres Risiko, hoher Nutzen". **Zu großen Teilen aus Vorhandenem zusammensetzbar:** Auto-Update, C12-Rollback (Snapshot + Rücknahme), CVE-Scanner und SP-6-Wirkungskontrolle existieren bereits; es fehlt der Vergleich davor. | M |
| GAP-7 | **Wirkungsradius einer Änderung, vorher** | Welche *anderen* Container eine Aktion mitreißt (Compose-Projekt, `depends_on`, Netzwerke). Anlass: am 2026-08-27 erstellte eine **neue** Override-Datei alle drei Authentik-Dienste neu, obwohl nur zwei geändert waren — die Datenbank startete unerwartet mit. Whiskers kann das vorher ausrechnen und als Trockenlauf zeigen. Natürliche Erweiterung von GAP-6. | S–M |
| GAP-8 | **Bedient der Dienst überhaupt?** | Zugriffszahlen, Statuscode-Verteilung und Antwortzeiten je Container — aus den **Access-Logs des Reverse Proxy** (Caddy / nginx-proxy-manager laufen in der Flotte), ohne die Anwendung anzufassen. Kein Marketing-Werkzeug: der operative Punkt ist, dass ein Container „Up (healthy)" melden und dabei durchgehend 502 ausliefern kann, oder dass Verkehr wegbricht, ohne dass irgendein Innensignal ausschlägt. Grenzt an GAP-2 (externe Checks) und schließt die Lücke zwischen „Prozess läuft" und „Dienst funktioniert". | M |

### Bewusst NICHT aufgenommen

| Idee | Warum nicht |
|---|---|
| **SEO-Analyse je Container** | Whiskers hat keinen Crawler, keine Keyword-Daten, keinen Wettbewerbsindex — es würde das schlecht machen, wofür es spezialisierte Werkzeuge gibt. Die operativ nützliche Teilmenge (TLS-Gültigkeit, Statuscode, `robots.txt`/Sitemap vorhanden, Weiterleitungsketten, Antwortzeit, Mixed Content) ist **Seiten-Gesundheit, nicht SEO**, und gehört als Prüfungstyp in GAP-2. So benannt verspricht sie nichts, was sie nicht hält. |
| **Besucher-Analytik** (eindeutige Besucher, Verweise, Verweildauer) | Anderes Produkt. Plausible/Umami machen es besser; der operative Teil steckt bereits in GAP-8. |
| **Deploy-Webhook aus der CI** | **Existiert bereits:** signierte Webhooks (`X-Hub-Signature-256`, GitHub-kompatibel) lösen `GitDeploy` und `Recreate` aus. Offen ist kein Code, sondern ein dokumentiertes Rezept — plus die Warnung, dass ein Webhook, der **Whiskers selbst** deployt, in EH-6 läuft: der Container killt sich mitten im Flip. Für Fremddienste unproblematisch. |

---

## 4c. Strang RES — Ressourcen und Lastverteilung (neu, 2026-08-27)

### Die Absage zuerst

Ein Verschiebe-Planer, der Container zwischen Hosts bewegt, ist ein Nachbau von Kubernetes/Nomad/Swarm —
Personenjahre, und ausdrücklich nicht, was Whiskers ist (GAP-1 heißt *Parität* mit K8s, nicht Ersatz). Dazu
kommt ein handfestes Hindernis: **in dieser Flotte gibt es nichts Zustandsloses zum Verschieben.** Jeder Dienst
hat Volumes. Dynamisches Auslagern scheitert nicht an der Planung, sondern an den Daten.

Die Pakete unten sind die Schnitte, die fast den ganzen Nutzen liefern, ohne diesen Weg zu gehen.

### Der Befund, der die Reihenfolge bestimmt

Plattenstand am 2026-08-27, aus `ServerMetrics`:

| Server | belegt | frei |
|---|---|---|
| burgcloud | 31,6 / 40 GB | **8,4 GB** |
| zirkuswagen | 28,2 / 40 GB | 11,8 GB |
| infomaniak | 13 / 19,7 GB | 7,5 GB (nach der Log-Bereinigung; vorher 4,7) |
| rabenhof | 18,3 / 40 GB | 21,6 GB |
| hetzner-apps | 15,7 / 40 GB | 24,2 GB |
| Badwolf (local) | 26,6 / 87 GB | 60,3 GB |

**Diese Flotte ist plattengebunden, nicht rechengebunden.** Der Engpass ist burgcloud mit 8,4 GB, nicht eine
CPU. Deshalb steht Platzrückgewinnung vor jeder Form von Verschiebung: Am selben Tag fanden sich 3 GB
unbegrenzte Logs auf einer einzigen Maschine, ohne dass jemand gesucht hätte.

| # | Paket | Was es löst | Aufwand |
|---|---|---|---|
| RES-1 | **Was kann hier weg, und wie viel bringt es** | Verwaiste Images, gestoppte Container, tote Volumes, Build-Caches, Logs ohne Limit — je Server, mit rückgewinnbarer Menge und einem Befehl, der es tut. Whiskers kennt das Image-Inventar bereits vom CVE-Scan; es fehlt nur die Rückseite. **Lastverteilung durch Nicht-Verschieben** — und das Einzige hier, das in dieser Flotte heute etwas ändert. | S |
| RES-2 | **Die eigene Arbeit lastabhängig planen** | Whiskers *ist* Last: es startet Trivy-Container auf den Zielhosts, liest Logs, sammelt Metriken. Der Vorfall vom 2026-08-26 war Whiskers, das einen Zweikern-Host niederdrückte. Diese Arbeit ist im Gegensatz zu Diensten frei planbar: keine schweren Läufe auf einem Server, der gerade bei 90 % steht; nie zwei parallel auf derselben Kiste; Schweres in die Nacht. Setzt SP-1 dort fort, wo es aufhört — vom „nicht zu viel gleichzeitig" zum „und nicht jetzt, nicht hier". | S–M |
| RES-3 | **Verschiebe-Empfehlung statt Verschiebung** | Aus 515.012 Container-Messpunkten über sechs Server: „burgcloud ist zu 79 % voll, größter Posten X; zirkuswagen läuft bei 12 % CPU mit 11,8 GB frei — X dorthin entlastet beide." Mit Belegen, der Mensch entscheidet. 90 % des Nutzens bei 5 % des Risikos, und es passt zum Charakter: Whiskers sagt Dinge, automatische Aktionen werden auf Wirkung geprüft (SP-6). | M |
| RES-4 | **Wegwerf-Rechner für wegwerfbare Arbeit** | Für einen Scanlauf über die ganze Flotte eine temporäre Cloud-Maschine hochziehen, dort scannen, wieder löschen. Kosten: Cent. Alternative: 40 Minuten spürbar langsamer burgcloud. Möglich, weil Whiskers als einziges Werkzeug **beides** hat — den Flottenblick und den Cloud-Zugang (`ICloudProvider`, Hetzner/Hostinger). Strikt begrenzt auf **zustandslose, wegwerfbare** Arbeit; niemals Dienste. Der Punkt, an dem Whiskers etwas kann, das die Konkurrenz strukturell nicht kann. | M–L |
| RES-5 | **Abhängigkeitskarte der Flotte** | Whiskers kennt Compose-Projekte, Netze und Ports. Was es nicht kennt, ist Stammeswissen: Whiskers braucht Authentik (OIDC) und VictoriaMetrics, mcpmcp braucht die CA. Als Daten hinterlegt wird daraus die Antwort auf „wenn dieser Server stirbt, was bricht?". | M |
| RES-6 | **Drain-Modus vor Wartung** | Vor einem Neustart sagen, was mitgeht, und Abhängige in einen bekannten Zustand bringen — statt es hinterher zu merken. Konkreter Anlass aus dem Betrieb: ein Reboot dieser Docker-Hosts schickt reverse-proxied Sites in 502, weil DNS-Einträge veralten; die Abhilfe ist heute ungeschriebenes Wissen. | S–M |

### Abhängigkeiten RES

```
SP-1 (Lastbudget) + SP-3 (Selbstbeobachtung) ──> RES-2 (ohne Lastzahlen keine lastabhängige Planung)
RES-5 (Abhängigkeitskarte) ──┬──> RES-6 (Drain weiß erst dadurch, wen es trifft)
                             └──> GAP-7 (Wirkungsradius vorher)
RES-1  unabhängig, sofort, größter Sofortnutzen
RES-4  braucht ICloudProvider (vorhanden) — eigenständiges Experiment
```

### Bewusst NICHT gebaut

| Idee | Warum nicht |
|---|---|
| **Verschiebe-Planer / Container-Migration zwischen Hosts** | Nachbau von Kubernetes/Nomad/Swarm, Personenjahre, und der ausdrückliche Nicht-Zweck von Whiskers. Zusätzlich hat in dieser Flotte jeder Dienst Volumes — das Hindernis sind die Daten, nicht die Planung. RES-3 liefert die Entscheidungsgrundlage, den Zug macht ein Mensch. |
| **Automatisches Auslagern von Diensten in die Cloud** | Dasselbe Zustandsproblem. RES-4 begrenzt sich deshalb strikt auf wegwerfbare Arbeit. |

---

## 5a. Stand 2026-08-27 und was noch einen Server braucht

**Wellen 0 bis 3 sind umgesetzt und seit 2026-08-27 auf Badwolf deployt** (864 Tests, Stand `bb1216d`). Erledigt: SP-1 (Abbruch, Lastbudget,
Circuit), SP-2, SP-3 (bis auf die Leerlaufmessung), SP-4, SP-5, SP-6 (WP1/WP2/WP5), SP-7, MCP. **GAP-1 bis
GAP-5 sind unberührt** — das sind Wochen, keine Stunden.

### Bewusst verworfen, mit Begründung im jeweiligen Plan

| Punkt | Warum nicht |
|---|---|
| SP-5 WP3 — Auto-Pause bei offenem Circuit | Der Circuit sperrt ohnehin; eine Pause obendrauf nähme die Überwachung weg, wenn der Server wackelt (Nutzerentscheidung) |
| SP-5 WP1.2 — Pause überlebt Neustart | Eine Pause reagiert auf etwas, das gerade passiert; überlebt sie den Neustart, überlebt sie ihren Grund (Nutzerentscheidung) |
| SP-5 WP4.3 — gestaffelter Wiederanlauf | Gemessen: es entsteht kein Sturm. Mechanismus gegen ein Problem, das die vorhandenen Mechanismen ausschließen |
| SP-6 WP3/WP4 — Rücknahme, Wiederholungssperre | Der Plan verlangt selbst vier Wochen Beobachtung zuerst |
| SP-1 — 16 der 24 Docker-Aufrufstellen | Interaktiv oder langlaufend; eine Circuit-Abweisung nähme dem Betreiber die Reparatur im ungünstigsten Moment |
| SP-4 WP1.3 — Schwellen je Server | Die rollende Baseline löst den Regelfall bereits; die verbleibende Ausnahme gibt es in dieser Flotte nicht, und die richtigen Zahlen kennt heute niemand — **erst messen, dann setzen** (Nutzerentscheidung) |

### Abnahmen, die ohne laufenden Server nicht zu erbringen sind

Diese Punkte sind **nicht offen im Sinne von unerledigt** — der Code steht und ist getestet. Was fehlt, ist
die Messung an der Wirklichkeit, und die braucht einen Deploy:

| Paket | Abnahme |
|---|---|
| SP-1 | Feldnachweis über 48 h auf zwei realen Servern (dockerd-CPU, Deskriptorzahl protokolliert) |
| SP-3 | Leerlaufmessung über 30 min mit und ohne Selbstmessung (WP6.1) |
| SP-4 | `stress-ng` auf dem Host → Meldung mit korrekter Ursachenklasse; künstlich verlangsamter Proxy → Latenzmeldung |
| SP-4 | **Fehlalarmquote** — der synthetische Prüfstand beweist, dass die Regeln anschlagen, nicht dass sie in einer normalen Woche schweigen |
| SP-5 | Wirksamkeit des Not-Aus am Zielserver: 0 neue Anfragen binnen 60 s |
| ~~SP-7~~ | 🟢 **erbracht 2026-08-27** — siehe unten |
| ~~alle~~ | 🟢 **erbracht 2026-08-27** — `tools/list` am laufenden Server liefert 78 Werkzeuge (vorher 67), die elf neuen sind dabei |

### 🟢 SP-7 abgenommen (2026-08-27, Badwolf)

**Größenmessung.** `stat -c %s` gegen `du --block-size=1`, alle zehn laufenden Container:

| Container | `stat` | `du` | Abweichung |
|---|---|---|---|
| authentik-worker-1 | 124.890.703 | 124.895.232 | +0,0 % |
| authentik-server-1 | 76.599.145 | 76.603.392 | +0,0 % |
| mcpmcp | 9.795.724 | 9.801.728 | +0,1 % |
| serverwatch | 4.213.088 | 4.218.880 | +0,1 % |
| node-exporter | 19.171 | 20.480 | +6,8 % |
| nginx-proxy-manager | 18.271 | 20.480 | +12,1 % |

Maximum 12,1 % gegen ein Kriterium von 20 %, und die Abweichung ist reine Blockrundung: Sie ist dort am
größten, wo die Datei am kleinsten ist, und bei den großen Dateien praktisch null. Genau die richtige
Richtung — bei den Containern, auf die es ankommt, stimmt die Zahl auf Promille.

**Behebungsbefehl.** Für die beiden Container, die ihn brauchen, sind alle drei Compose-Marken gesetzt
(`project=authentik`, `service=worker|server`, `working_dir=/opt/authentik`) → Whiskers erzeugt den
Compose-Block plus `cd /opt/authentik && docker compose up -d --force-recreate worker`. Läuft ohne
Nacharbeit.

**Der Wächter schlägt an, nicht nur der Test.** Im Feld gemeldet:
`Unbounded log on LupusMalus (Infomaniak)/ghostunnel: 1.66 GB`. Dieselbe Container-Instanz, die am selben Tag
die neue CVE-Veraltungsmetrik markiert hat (Scandaten vom 29. Juli). Zwei unabhängig gebaute Signale zeigen
auf denselben Container — offen, warum sein CVE-Scan seit vier Wochen nicht läuft.

Nebenbefund: `authentik-worker-1` (124 MB) und `authentik-server-1` (76 MB) laufen **ganz ohne Log-Limit**
(`LogConfig.Config` ist leer), `mcpmcp` dagegen mit `max-size=10m, max-file=3`.

Der letzte Punkt betrifft alle Pakete gemeinsam und hat einen konkreten Anlass: Von 0.12.0 bis 0.13.0 hat der
MCP-Server **null** Werkzeuge ausgeliefert, und kein Test, kein Log und kein Alarm hat es gesagt. Der
`McpServedSurfaceTests` bootet inzwischen die echte Anwendung und fragt `tools/list` ab — aber die Gegenprobe
am tatsächlich laufenden Server bleibt der einzige Beweis, der zählt.

---

## 6. Querverweise

- [Vorfallsbericht 2026-08-26](../reviews/2026-08-26-logmonitor-dockerd-cpu-incident.md) — Ursache, Messwerte, Prüfkriterium
- [attackResponse.md](attackResponse.md) — der Angriffs-Strang; AR-1 (Incident-Objekt) trägt auch die selbstverschuldeten Vorfälle
