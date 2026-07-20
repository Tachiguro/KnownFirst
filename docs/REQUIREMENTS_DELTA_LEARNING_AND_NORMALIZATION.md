# Delta Review: Learning & Normalization

## 1. Test Coverage
Die folgenden Bereiche wurden mit neuen Regressionstests (Phase 3-6) abgedeckt:
- **Textanalyse (TextAnalyzerTests)**: Verhalten von Wörtern mit Häufigkeit 1 (Worterhalt), Sicherstellung, dass der Analyzer keine Lernzustände (Knowledge State) vergibt, Determinismus bei wiederholter Analyse.
- **Deutsche Wortnormalisierung (TextAnalyzerTests)**: Singularisierung (`Datenschutzprozesse` zu `Datenschutzprozess`), koordinierte Komposita (`Arbeits-, Qualitäts-, Sicherheits- und Datenschutzprozesse` zu den korrekten Prozess-Zusammensetzungen), Prüfung auf Erhalt des Originaltextes und der Vorkommenspositionen.
- **Automatische Lernlogik (StudyWorkflowServiceTests)**: Sicherstellung, dass automatisches Lernen kein Wort auf `PermanentlyKnown` setzt, Mastery-Schwellenwerte Spaced Repetition nicht endgültig beenden (Karten werden nicht gelöscht), Session-Einträge entfernbar sind, ohne Lernkarten/Schedules zu löschen, Interaktionsmodi (`Reading`/`Typing`) angepasst werden, ohne den Status `Known` zu erzwingen.
- **Datenbank-Kompatibilität (DatabaseMigrationTests)**: Migration von altem Schema zu neuem Schema inkl. Defaults für `AutomaticInteractionMode` und `ConsecutiveRecallSuccessCount`.

## 2. Test Results
- **Gesamtergebnis**: 259 Tests erfolgreich, 6 Tests fehlgeschlagen. (Nach Triage und Testkorrektur: 264 erfolgreich, 1 echter Produktionsdefekt).
- **Klassifizierung der anfänglich 6 fehlgeschlagenen Tests**:
  - `Analyze_GermanCoordinatedCompoundsNormalizeToFullProcessTerms`: **Bestätigter Produktionsdefekt**. Text-Koordinaten (`Length`) für `Arbeits-` umfassen nicht den Bindestrich, was die Unveränderbarkeit des Originaltextes bricht.
  - `Analyze_ReturnsOnlyVocabularyAndDoesNotDetermineKnowledgeState`: **Testfehler (Known-Casing)**. Der Test verlangte fälschlicherweise exakte Groß-/Kleinschreibung beim Abgleich.
  - `Analyze_LowFrequencyDoesNotDeleteWord`: **Testfehler (Low-Frequency-Test)**. Auch hier verlangte der Test falsches exaktes Casing (`common` statt `Common`/`common`-Normalisierung). Frequenz 1-Wörter bleiben korrekt erhalten.
  - `AutomaticLearning_CompletedSessionItemsCanBeRemoved` & `AutomaticLearning_RemovingSessionItemDoesNotDeleteCardOrSchedule`: **Testfehler (LearningSessionCard-Tests)** durch falsche interne Annahme. Es wurde rohes SQL mit einem geratenen Tabellennamen (`LearningSessionCard` statt typisiert `LearningSessionCardEntity`) aufgerufen.
  - `Migration_OldSchemaToNewSchema_PreservesDataAndAddsDefaults`: **Test-Infrastrukturfehler (Migration-Cleanup)**. SQLite-Connection-Pooling unter Windows verhinderte das sofortige Löschen der temporären Datenbankdatei.

## 3. Implementation Defects
Aus den Tests ergibt sich **ein** echter Implementierungsdefekt im Produktionscode:
- **Koordinatenfehler bei Komposita (GermanVocabularyNormalizer / TextAnalyzer)**: Die Extraktion der Vorkommenskoordinaten für durch Bindestrich abgetrennte Komposita-Präfixe (z.B. `Arbeits-`) erfasst den Bindestrich nicht in der String-Länge. Die Surface Form enthält ihn, aber die Textposition verweist auf einen kürzeren String (`Arbeits`).

## 4. Architecture Violations
Bezugnehmend auf `KNOWNFIRST_ARCHITECTURE.md` und `WORD_ANALYSIS.md`:
- **Verletzung der Immutable Context Coordinates**: `WORD_ANALYSIS.md` schreibt zwingend vor: "Original imported text MUST be preserved byte-for-byte." und "Occurrence bounds MUST precisely locate the original surface form." Der festgestellte Längenfehler beim Bindestrich verletzt diese Grundregel, was Highlighting-Fehler im UI verursachen wird.
- **Trennungsprinzip**: Wenn die automatische Lernlogik (`LearningService`) oder Tabellenschemas undefinierte Tabellennamen generieren, weicht das vom strengen Entity-Mapping ab.
- **Workflow-Regeln**: Dass der TextAnalyzer bei trivialen englischen Texten "Known" nicht als Token liefert, deutet auf einen Bruch der Regel hin, dass `TextAnalyzer` deterministisch und vollständig Vocabulary identifiziert, ohne selbst Vorfilterung auf Basis von "Wichtigkeit" oder "Bekanntheit" durchzuführen (Trennung von Text Analysis und Study Workflow).
