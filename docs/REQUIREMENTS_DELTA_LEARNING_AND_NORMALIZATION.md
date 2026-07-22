# Delta Review: Learning & Normalization

> **Historical verification record:** The 265-test result below belongs to the
> learning/normalization review completed at `8a98eb1` on 2026-07-20. It is
> not the current suite total. The verified project-wide result on 2026-07-22 is
> recorded in [PROJECT_STATE.md](PROJECT_STATE.md).

## 1. Test Coverage
Die folgenden Bereiche wurden mit neuen Regressionstests (Phase 3-6) abgedeckt:
- **Textanalyse (TextAnalyzerTests)**: Verhalten von Wörtern mit Häufigkeit 1 (Worterhalt), Sicherstellung, dass der Analyzer keine Lernzustände (Knowledge State) vergibt, Determinismus bei wiederholter Analyse.
- **Deutsche Wortnormalisierung (TextAnalyzerTests)**: Singularisierung (`Datenschutzprozesse` zu `Datenschutzprozess`), koordinierte Komposita (`Arbeits-, Qualitäts-, Sicherheits- und Datenschutzprozesse` zu den korrekten Prozess-Zusammensetzungen), Prüfung auf Erhalt des Originaltextes und der Vorkommenspositionen.
- **Automatische Lernlogik (StudyWorkflowServiceTests)**: Sicherstellung, dass automatisches Lernen kein Wort auf `PermanentlyKnown` setzt, Mastery-Schwellenwerte Spaced Repetition nicht endgültig beenden (Karten werden nicht gelöscht), Session-Einträge entfernbar sind, ohne Lernkarten/Schedules zu löschen, Interaktionsmodi (`Reading`/`Typing`) angepasst werden, ohne den Status `Known` zu erzwingen.
- **Datenbank-Kompatibilität (DatabaseMigrationTests)**: Migration von altem Schema zu neuem Schema inkl. Defaults für `AutomaticInteractionMode` und `ConsecutiveRecallSuccessCount`.

## 2. Test Results
- **Gesamtergebnis**: 265 Tests erfolgreich, 0 Tests fehlgeschlagen.
- **Initial findings and resolution**:
  - `Analyze_GermanCoordinatedCompoundsNormalizeToFullProcessTerms`: Der Koordinatenfehler bei deutschen koordinierten Komposita wurde behoben. `Arbeits-` behält Surface Form und Originaltextlänge inklusive Bindestrich bei der Zuweisung. Der zugehörige Regressionstest besteht.
  - `Analyze_ReturnsOnlyVocabularyAndDoesNotDetermineKnowledgeState` & `Analyze_LowFrequencyDoesNotDeleteWord`: `Known` und `Common/common` waren Testfehler durch eine unnötig case-sensitive Assertion. Es besteht kein daraus abgeleiteter Architekturverstoß.
  - `AutomaticLearning_CompletedSessionItemsCanBeRemoved` & `AutomaticLearning_RemovingSessionItemDoesNotDeleteCardOrSchedule`: Die Fehler entstanden durch einen im Test geratenen Tabellennamen. Die korrigierten Tests verwenden die typisierte SQLite-Schnittstelle. Es besteht kein nachgewiesener Produktionsfehler.
  - `Migration_OldSchemaToNewSchema_PreservesDataAndAddsDefaults`: Die Migration erhält vorhandene Daten und neue Felder bekommen definierte Default-Werte. Die Migration ist testbar mit einer isolierten Temp-Datenbank. Der Windows-Dateilock war ein Testinfrastrukturproblem. Der Cleanup verwendet jetzt einen deterministischen Connection-Lifecycle und Pool-Reset. Es bleibt kein offener Migrationsdefekt.

## 3. Implementation Defects
Aus den durchgeführten Validierungen verbleiben **keine** offenen echten Implementierungsdefekte im Produktionscode.

## 4. Architecture Violations
Bezugnehmend auf `KNOWNFIRST_ARCHITECTURE.md` und `WORD_ANALYSIS.md`:
- Es bestehen nach den Korrekturen keine identifizierten Architekturverstöße mehr. Die Originaltext-Invariante für Koordinaten wird durch den Fix für Komposita eingehalten. Die Trennung von Text Analysis und Study Workflow ist gesichert, da TextAnalyzer keine Filterung nach Knowledge State durchführt.
