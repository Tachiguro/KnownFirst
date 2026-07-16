# KnownFirst – Windows-GUI-Testplan

**Zweck:** Die Windows-Debug-Version reproduzierbar prüfen, bevor alle Fehler gesammelt an Codex zurückgegeben werden.

Vor dem Lauf notieren: Branch, Commit, Datum, Windows-Version, App-Sprache, Theme, Vorbereitungslimit, Kartenrichtung und Netzwerkstatus.

Status je Test: **Bestanden / Fehlgeschlagen / Nicht ausführbar**. Bei Fehlern Screenshot, Eingabetext, exakte Klickfolge und tatsächliches Verhalten notieren.


---

## WIN-SET-001 – Sprache sofort wechseln
**Vorbereitung:** App-Daten zurücksetzen und Einstellungen öffnen.

**Schritte**
1. Deutsch wählen.
2. Gesamte sichtbare Oberfläche prüfen.
3. Englisch wählen.
4. App schließen und neu öffnen.

**Erwartung**
- Sprache wechselt ohne Neustart vollständig.
- Keine gemischten deutschen/englischen Texte.
- Manuelle Auswahl bleibt nach Neustart erhalten.


---

## WIN-SET-002 – Werkseinstellungen
**Vorbereitung**
- Windows-Anzeigesprache: Deutsch.
- KnownFirst: Englisch, Dark, Limit 50, Kartenrichtung Begriff→Bedeutung.

**Schritte**
1. Werkseinstellungen zurücksetzen.
2. Bestätigung ausführen.
3. Einstellungen kontrollieren.
4. App neu starten.

**Erwartung**
- Sprache: Deutsch (Systemsprache).
- Theme: System.
- Vorbereitungslimit: 10.
- Kartenrichtung: Beide Richtungen.
- Datenbank/Lernstand leer.
- Keine alte Auswahl bleibt optisch markiert.


---

## WIN-WF-001 – Offene Prüfung blockiert andere Prozesse
**Titel:** `Blocking Review`

```text
A firewall protects systems.
```

**Schritte**
1. Importieren und analysieren.
2. Keine Entscheidung treffen.
3. Lernen und Text importieren testen.
4. Einstellungen öffnen und wieder verlassen.

**Erwartung**
- Review startet automatisch.
- Lernen und neuer Import sind gesperrt oder führen zurück zum Review.
- Einstellungen bleiben erreichbar.
- Danach erscheint wieder dieselbe offene Prüfung.


---

## WIN-WF-002 – Review nach Neustart fortsetzen
1. In `WIN-WF-001` mindestens ein Wort entscheiden.
2. App schließen und neu öffnen.

**Erwartung**
- Bereits entschiedene Wörter werden nicht wiederholt.
- Review startet beim ersten ungelösten Kandidaten.
- Fortschritt stimmt.


---

## WIN-WF-003 – Import verwerfen
1. Offene Prüfung verwerfen.
2. Destruktive Bestätigung ausführen.
3. App neu starten.

**Erwartung**
- Sitzung und importbezogene Daten sind entfernt.
- Frühere Known-Marker bleiben erhalten.
- Keine Fortsetzungsaufforderung nach Neustart.


---

## WIN-TOK-001 – Technische Tokens und Ausschlüsse
**Vorbereitung:** App-Daten zurücksetzen.

**Titel:** `Technical Tokens`

```text
OAuth2 uses IPv6. SHA-256 protects data. CVE-2026-12345 identifies a vulnerability. Contact test@example.com or visit https://example.com. The values are 42 and 2026.
```

**Müssen als Kandidaten erhalten bleiben**
- OAuth2
- IPv6
- SHA-256
- CVE-2026-12345

**Dürfen nicht als Kandidat erscheinen**
- test@example.com
- https://example.com
- 42
- 2026

**Zusätzlich:** Hervorhebung exakt; keine beschädigten Bindestriche.


---

## WIN-TOK-002 – Deutsch, Umlaute und ß
**Vorbereitung:** Reset.

**Titel:** `German Unicode`

```text
Die IT schützt das Netzwerk. Größe, Straße und Übertragung bleiben erhalten. Häuser stehen neben einem Haus. Die Netzwerke schützen Daten.
```

**Erwartung**
- schützt, Größe, Straße, Übertragung und Häuser sind exakt dargestellt.
- IT bleibt von it getrennt.
- Keine beschädigten Zeichen oder falschen Offsets.


---

## WIN-TOK-003 – Großschreibung und Akronyme
**Vorbereitung:** Reset.

**Titel:** `Case Identity`

```text
IT protects data. it protects data. US teams use security. us teams use security. Network systems use network security. NETWORK traffic is visible.
```

**Erwartung**
- IT ≠ it.
- US ≠ us.
- Network/network dürfen als gewöhnliche Großschreibungsvarianten gruppiert werden.
- NETWORK darf nicht blind als bestätigtes Akronym gelten.


---

## WIN-CTX-001 – Doppelte Sätze
**Vorbereitung:** Reset.

**Titel:** `Duplicate Contexts`

```text
Security protects data. Security protects data. Security protects networks. Networks protect data. Security protects systems.
```

**Erwartete Vorkommen**
- Security/security: 4
- protects: 4
- data: 3
- Networks/networks: 2
- protect: 1
- systems: 1

**Security-Kontexte**
1. Security protects data.
2. Security protects networks.
3. Security protects systems.

Vorkommen = 4, unterschiedliche Kontexte = 3. Der doppelte Satz wird nur einmal angezeigt.


---

## WIN-CTX-002 – Dreifach identischer Satz
**Vorbereitung:** Reset.

**Titel:** `Triple Duplicate`

```text
The firewall blocks malware. The firewall blocks malware. The firewall blocks malware. Malware damages systems. The firewall protects systems.
```

**Erwartung**
- firewall: 4 Vorkommen, 2 Kontexte.
- blocks: 3 Vorkommen, 1 Kontext.
- malware: 4 Vorkommen, 2 Kontexte.
- systems: 2 Vorkommen, 2 Kontexte.


---

## WIN-KNW-001 – Ersten Text vollständig Known
**Vorbereitung:** Reset.

**Text A – Titel:** `Known Vocabulary A`

```text
secure systems protect data. secure networks protect systems. data and networks protect systems.
```

Alle Kandidaten **Bekannt** markieren.

**Erwartung**
- Keine Lernkarten.
- Volltext, Satzspannen und Occurrences werden bereinigt.
- Nur minimale Known-Marker bleiben.
- Meldung: `Du kennst alle Wörter in diesem Text. Der Text wurde nicht gespeichert.`


---

## WIN-KNW-002 – Anderer Text mit denselben bekannten Wörtern
**Voraussetzung:** `WIN-KNW-001`, kein Reset.

**Text B – Titel:** `Known Vocabulary B`

```text
data and systems protect secure networks. secure systems and networks protect data.
```

**Erwartung**
- Kein Review.
- Meldung: `Alle Wörter sind bereits bekannt. Der Text wurde nicht gespeichert.`
- Kein Document, keine ReviewSession, keine Occurrences, keine Lernkarte und keine Zähleränderung.


---

## WIN-KNW-003 – Bekannte Wörter plus genau ein neues Wort
**Voraussetzung:** `WIN-KNW-001`, kein Reset.

**Text C – Titel:** `One New Word`

```text
secure systems protect encrypted data and networks.
```

**Erwartung**
- Review zeigt ausschließlich `encrypted`.
- Wird encrypted Bekannt: Text wird bereinigt.
- Wird encrypted Unbekannt: genau ein Wort geht in die Vorbereitung.


---

## WIN-LEM-001 – risk und risks
**Vorbereitung:** Reset.

**Titel:** `Risk Lemma`

```text
Risk reduces risks. Risks increase risk. Risky systems create risks.
```

**Zielverhalten**
- Kanonisches Lemma: `risk`.
- Aliase: Risk, risk, Risks, risks.
- Aggregierte Vorkommen risk/risks: 5.
- `Risky` bleibt separat.
- Wörterbuchabfrage bevorzugt `risk`.
- Lernkarte zeigt nicht nur `plural of risk`.
- Kontext zeigt weiterhin die tatsächliche Form `risks`, falls diese vorkam.


---

## WIN-LEM-002 – Englische Flexionsformen
**Vorbereitung:** Reset.

**Titel:** `Protect Inflections`

```text
Systems protect data. A firewall protects data. Protected systems remain safe. Protecting data reduces risk. Risky behavior remains dangerous.
```

**Ziel für Fehlerbericht**
- protect, protects, protected und protecting verweisen bei sicherer Lemmatisierung auf `protect`.
- Risky bleibt getrennt von risk.
- Keine aggressive Stammverkürzung.
- Kontexte behalten die tatsächliche Oberflächenform.


---

## WIN-PREP-001 – Häufigste Wörter zuerst
**Vorbereitung:** Reset; Vorbereitungslimit 3; Kartenrichtung Beide Richtungen.

**Titel:** `Frequency Priority`

```text
network network network network network. encryption encryption encryption encryption. authentication authentication authentication. risk risk risk. firewall firewall. malware.
```

Nur diese Wörter **Unbekannt** markieren.

**Erwartete Reihenfolge**
1. network – 5
2. encryption – 4
3. authentication – 3
4. risk – 3
5. firewall – 2
6. malware – 1

Erster Batch bei Limit 3: network, encryption, authentication.


---

## WIN-PREP-002 – Automatische Vorbereitung und Fortsetzen
**Voraussetzung:** `WIN-PREP-001`.

1. Wörter vorbereiten starten.
2. Automatisch online wählen.
3. Datenschutzhinweis bestätigen.
4. Ein Ergebnis übernehmen.
5. Bei einem Ergebnis Bearbeiten öffnen.
6. Bei einem Ergebnis Andere Bedeutung öffnen.
7. Vorbereitung abbrechen.
8. App neu starten und fortsetzen.

**Erwartung**
- Kein API-Key.
- Automatisches Ergebnis ohne Tippen übernehmbar.
- Bearbeiten optional.
- Alternative Bedeutungen auswählbar.
- Bereits akzeptierte Wörter werden nach Neustart nicht wiederholt.


---

## WIN-DICT-001 – Akronyme aus Originaltext
**Vorbereitung:** Reset.

**Titel:** `Acronym Preparation`

```text
Information Security Management System (ISMS) reduces risks. Multi-Factor Authentication (MFA) protects accounts. A firewall blocks malware.
```

ISMS, MFA, risk/risks, firewall und malware **Unbekannt** markieren.

**Erwartung**
- ISMS → Information Security Management System.
- MFA → Multi-Factor Authentication.
- Langformen kommen aus dem Originaltext und haben Vorrang.
- risks wird als Lernidentität `risk` vorbereitet, nicht nur mit `plural of risk`.


---

## WIN-DICT-002 – Mehrdeutigkeit sinnvoll anzeigen
**Vorbereitung:** Reset.

**Titel:** `Audit Meanings`

```text
The audit identified risks. We audit systems annually. A firewall blocks malware.
```

audit und firewall **Unbekannt** markieren.

**Erwartung**
- Bei audit darf die Warnung `Mehrere Bedeutungen könnten ...` erscheinen.
- Warnung nur in Vorbereitung/Auswahl, nicht dauerhaft auf jeder Lernkarte.
- Bei firewall keine pauschale Warnung, wenn Ergebnis eindeutig ist.


---

## WIN-DICT-003 – Quellenangabe kompakt
**Zielverhalten**
- Normale Ansicht: `Quelle: Wiktionary`.
- Vollständige Details eingeklappt: Domain, Seitentitel, Revision, Lizenz, Attribution.
- Diese drei Zeilen sollen nicht auf jeder Karte permanent Platz belegen:

```text
Several meanings may fit this context.
Source: en.wiktionary.org, “audit”
Wiktionary contributors; text available under the Creative Commons Attribution-ShareAlike license.
```

Lizenzdaten bleiben erhalten, werden nur visuell eingeklappt.


---

## WIN-DICT-004 – Kein Wörterbuchergebnis
**Vorbereitung:** Reset.

**Titel:** `Missing Dictionary Entry`

```text
QXZGuard protects systems.
```

QXZGuard Unbekannt, Rest Bekannt.

**Erwartung**
- Kein erfundener Eintrag.
- Klarer Fehlertext.
- Aktionen: Retry, Manuell, Später.
- Ein fehlendes Ergebnis blockiert nicht die gesamte Vorbereitung.


---

## WIN-LRN-001 – Begriff zu Bedeutung
**Voraussetzung:** Mindestens zwei vorbereitete Wörter.

1. Lernen starten.
2. Prüfen, dass Antwort vor `Antwort anzeigen` verborgen ist.
3. Antwort anzeigen.
4. `Nochmal` wählen.
5. Restliche Karten bearbeiten.
6. Prüfen, ob die Karte höchstens einmal am Ende wiederkommt.
7. Dann `Gut` wählen.

**Erwartung**
- Kein Skip.
- Keine Endlosschleife.
- Bewertung wird sofort gespeichert.
- Quelle ist kompakt.


---

## WIN-LRN-002 – Bedeutung zu Begriff mit Schreibfehler
1. Bedeutung-zu-Begriff-Karte öffnen.
2. Einen Buchstaben absichtlich falsch eingeben.
3. Prüfen.
4. Korrekturansicht kontrollieren.
5. Später korrekt eingeben und Gut wählen.

**Erwartung**
- Falsch geschrieben wird nicht akzeptiert.
- Unterschiedliche Zeichen sind verständlich markiert.
- Fehler wird als Nochmal gespeichert.
- Richtige Antwort erlaubt Schwer/Gut/Einfach.
- Beide Kartenrichtungen haben unabhängige Zeitpläne.


---

## WIN-LRN-003 – Akronym-Großschreibung
Bei MFA/ISMS zuerst `mfa` oder `isms`, danach korrekt `MFA` oder `ISMS` eingeben.

**Erwartung:** Kleinschreibung ist bei Akronymen falsch; korrekte Großschreibung wird akzeptiert.


---

## WIN-LRN-004 – Lernsession fortsetzen
1. Sitzung beginnen und mehrere Karten bewerten.
2. App schließen und neu öffnen.
3. Fortsetzen.

**Erwartung**
- Keine neue Sitzung.
- Keine doppelte Bewertung.
- Bereits erledigte Karten werden nicht erneut als neu gezählt.


---

## WIN-CLEAN-001 – Dauerhaft bekannt und Dokumentlöschung
**Vorbereitung:** Reset.

**Titel:** `Cleanup Single Word`

```text
Encryption.
```

1. Encryption Unbekannt.
2. Vorbereiten und Lernen öffnen.
3. `Dauerhaft als bekannt markieren`.
4. Bestätigen.
5. Diagnostics prüfen.
6. Text erneut importieren.

**Erwartung**
- Bestätigung erforderlich.
- Beide Kartenrichtungen, persönliche Definition, Übersetzung, Kontexte, Frequenz und Schedule werden entfernt.
- Minimaler Known-Marker bleibt.
- Dokument wird vollständig gelöscht.
- Reimport fragt Encryption nicht erneut und speichert den Text nicht.


---

## WIN-CLEAN-002 – Teilweise Bereinigung
**Vorbereitung:** Reset.

```text
Encryption protects authentication.
```

Encryption und authentication Unbekannt, beide vorbereiten. Nur Encryption dauerhaft bekannt markieren.

**Erwartung**
- Encryption-Lerndaten verschwinden.
- authentication bleibt aktiv.
- Dokument bleibt, solange authentication Kontext benötigt.
- Nach dauerhafter Bekannt-Markierung von authentication wird es gelöscht.


---

## WIN-ERR-001 – Offline während Vorbereitung
1. Unknown-Wort erzeugen.
2. Netzwerk deaktivieren.
3. Automatisch online starten.
4. Manuelle Vorbereitung öffnen.
5. Netzwerk aktivieren und Retry.

**Erwartung**
- Kein leerer Fehler.
- Verständlicher lokalisierter Text.
- Retry und manuelle Vorbereitung funktionieren.
- Cache bleibt offline nutzbar.


---

## WIN-ERR-002 – Doppelklickschutz
Schnell doppelt klicken bei Save and analyze, Bekannt, Unbekannt, Übernehmen, Antwort anzeigen und Bewertungen.

**Erwartung:** keine doppelten Dokumente, Entscheidungen, Karten oder Ratings; kein übersprungener Kandidat.


---

## WIN-UI-001 – Fenstergrößen
Bei schmalem, normalem und maximiertem Fenster sowie 125/150 % Skalierung prüfen.

**Erwartung**
- Keine Überlappung.
- Buttons erreichbar.
- Dialoge passen.
- Fokusrahmen sichtbar.
- Keine unnötigen horizontalen Scrollbalken.


---

# Bekannter Android-Release-Blocker

## AND-REL-001 – Release startet nicht

**Beobachtung**

1. `KnownFirst wird vorbereitet...`
2. Rote Fehlerkarte zeigt nur `!`, Retry und Schließen, aber keinen Text.
3. Nach Retry: `Die aktive Überprüfung wird geprüft...`
4. Danach derselbe leere Fehlerzustand.

**Schweregrad:** Blocker.

**Soll**
- Release startet erfolgreich.
- Bei Fehler: lokalisierter Text, Startphase, kurze Fehlerkennung, Retry und `Diagnosedetails kopieren`.
- Tatsächliche Exception und InnerException werden protokolliert.
- Datenbankmigration, Startup-Maintenance, Workflow-Recovery und Release-Trimming/AOT müssen getrennt diagnostizierbar sein.

---

# Ergebnisübersicht

| Test-ID | Status | Beobachtung | Screenshot |
|---|---|---|---|
| WIN-SET-001 | ☐ | | |
| WIN-SET-002 | ☐ | | |
| WIN-WF-001 | ☐ | | |
| WIN-WF-002 | ☐ | | |
| WIN-WF-003 | ☐ | | |
| WIN-TOK-001 | ☐ | | |
| WIN-TOK-002 | ☐ | | |
| WIN-TOK-003 | ☐ | | |
| WIN-CTX-001 | ☐ | | |
| WIN-CTX-002 | ☐ | | |
| WIN-KNW-001 | ☐ | | |
| WIN-KNW-002 | ☐ | | |
| WIN-KNW-003 | ☐ | | |
| WIN-LEM-001 | ☐ | | |
| WIN-LEM-002 | ☐ | | |
| WIN-PREP-001 | ☐ | | |
| WIN-PREP-002 | ☐ | | |
| WIN-DICT-001 | ☐ | | |
| WIN-DICT-002 | ☐ | | |
| WIN-DICT-003 | ☐ | | |
| WIN-DICT-004 | ☐ | | |
| WIN-LRN-001 | ☐ | | |
| WIN-LRN-002 | ☐ | | |
| WIN-LRN-003 | ☐ | | |
| WIN-LRN-004 | ☐ | | |
| WIN-CLEAN-001 | ☐ | | |
| WIN-CLEAN-002 | ☐ | | |
| WIN-ERR-001 | ☐ | | |
| WIN-ERR-002 | ☐ | | |
| WIN-UI-001 | ☐ | | |

---

# Fehlerbericht-Vorlage

```text
ID:
Severity:
Branch/commit:
Platform:
Build configuration:
Preconditions:
Exact input text:
Exact click sequence:
Expected result:
Actual result:
Reproducibility:
Screenshot/video:
Relevant diagnostics:
```
