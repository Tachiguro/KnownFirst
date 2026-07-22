# Android Online-Suche Crash-Testplan

Dieser Testplan stellt sicher, dass die App bei Online-Suchanfragen (Wortvorbereitung) unter extremen Bedingungen nicht abstürzt und sich deterministisch verhält. Die Tests sind manuell auf einem physischen Android-Gerät oder Emulator auszuführen.

## Voraussetzungen
- Aktuelle Debug- oder Release-Build auf einem Android-Gerät installiert.
- App gestartet, bekannte Wörter vorhanden.
- Zu einem "Unbekannten Wort" navigieren und den Button "Online suchen" bereithalten.

## Testfälle

1. **Online-Suche mit funktionierendem Netzwerk**
   - *Aktion*: "Online suchen" bei stabiler WLAN-/Mobilfunkverbindung tippen.
   - *Erwartet*: Das Ergebnis wird erfolgreich geladen und angezeigt. Kein Absturz.

2. **Flugmodus**
   - *Aktion*: Gerät in den Flugmodus versetzen (kein WLAN/Mobilfunk) und "Online suchen" tippen.
   - *Erwartet*: Eine kontrollierte Fehlermeldung (Netzwerkfehler) erscheint. Die App bleibt stabil.

3. **Netzwerkverlust während der Anfrage**
   - *Aktion*: "Online suchen" tippen und sofort während des Ladevorgangs das WLAN deaktivieren.
   - *Erwartet*: Der Timeout oder Verbindungsabbruch wird abgefangen. Die UI zeigt eine Fehlermeldung.

4. **Langsames Netzwerk**
   - *Aktion*: Netzwerkverbindung auf Edge/3G drosseln (z.B. über Entwickleroptionen) und "Online suchen" tippen.
   - *Erwartet*: Ladespinner dreht sich; die Suche bricht eventuell nach dem konfigurierten Timeout sicher ab oder wird extrem verzögert abgeschlossen.

5. **Schnelles mehrfaches Drücken**
   - *Aktion*: Den Button "Online suchen" mehrfach extrem schnell hintereinander antippen (Doppeltipp/Spamming).
   - *Erwartet*: Der Button wird nach dem ersten Klick deaktiviert (disabled). Keine überlappenden oder crashenden Parallelanfragen.

6. **Kein Suchtreffer**
   - *Aktion*: Nach einem erfundenen oder nicht existierenden Wort suchen.
   - *Erwartet*: Eine Meldung "Wort nicht gefunden" (NotFound) wird angezeigt. Kein Crash beim Parsen.

7. **Seitenwechsel während der Anfrage**
   - *Aktion*: "Online suchen" tippen und sofort die Seite verlassen (z.B. Zurück-Button des Systems oder Hamburger-Menü).
   - *Erwartet*: Die Operation wird sicher abgebrochen (TaskCanceledException). Kein Absturz durch verwaiste UI-Updates.

8. **Erwartete sichere Fehlermeldung**
   - *Aktion*: Jeden der obigen Fehlerschritte prüfen.
   - *Erwartet*: Keine rohen Stacktraces oder englischen Exception-Texte in der UI, sondern saubere, lokalisierte Rückmeldungen.

9. **Erneute Suche nach einem Fehler**
   - *Aktion*: Nach einem aufgetretenen Fehler (z. B. Flugmodus) das Netzwerk wiederherstellen und erneut "Online suchen" tippen.
   - *Erwartet*: Die Suche startet normal, der vorherige Fehlerstatus blockiert die Anfrage nicht dauerhaft (Pending-Lock aufgehoben).

10. **Prüfung des Diagnoseberichts**
    - *Aktion*: Nach Durchlauf der Tests in der App auf "Diagnosebericht" tippen / den Bericht exportieren.
    - *Erwartet*: Die ausgelösten Fehler (Timeouts, Exceptions) sind in den JSONL-Logs vorhanden (Event enrichment.provider-crash oder http.timeout), enthalten aber keine sensiblen Nutzertexte aus dem importierten Material.
