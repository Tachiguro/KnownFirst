# Wikipedia JSON API Client

## Kontext
KnownFirst benötigt einen Fallback-Provider für Begriffe, die im primären Provider (Wiktionary) nicht gefunden werden. Wikipedia eignet sich hierfür, ist jedoch in seiner HTML-Struktur stark variabel.

## Entscheidung
Für die Anbindung an Wikipedia wird ausschließlich die offizielle MediaWiki Action API über JSON (`formatversion=2`) verwendet. Ein `WikipediaHtmlParser` ist architektonisch untersagt.

## Begründung
Die HTML-Struktur von Wikipedia-Artikeln ändert sich häufig und ist extrem komplex (Infoboxen, Navboxen, verschiedene Tabellenlayouts). Ein HTML-Scraping wäre fehleranfällig und wartungsintensiv. Die MediaWiki JSON-API bietet standardisierte Felder (`extract`, `pageprops.disambiguation`, `langlinks`), die robust maschinenlesbar sind.

## Implementierungsdetails
1. **Source Generation:** Die Deserialisierung der JSON-Antworten erfolgt ausschließlich über `System.Text.Json` mit Source Generation (`JsonSourceGenerationOptions`), um AOT-Kompatibilität und Trimming-Sicherheit für Android-Release-Builds zu gewährleisten. Reflektion ist vollständig verboten.
2. **Provider Identität:** Der Wikipedia-Client ist vorerst unabhängig vom bestehenden `ILexicalLookupProvider`. Er implementiert `IWikipediaApiClient` für die direkte Kommunikation. Die providerneutrale Integration in den `LexicalLookupProviderResolver` erfolgt in einem späteren Arbeitspaket.
3. **Deterministische Tests:** Die Testabdeckung wird ausschließlich durch lokale JSON-Fixtures erzeugt, welche die Struktur der echten Wikipedia-API abbilden. Es gibt keine Live-Netzwerktests.
4. **Fehlerbehandlung:** HTTP-Fehler, Timeouts und Rate-Limits (HTTP 429) werden im Client abgefangen und auf das interne Enum `WikipediaArticleStatus` gemappt, damit aufrufende Schichten providerunabhängige Entscheidungen treffen können. Timeouts sind rein intern, Aufrufer-Cancellation wird weitergereicht. Es gibt keine automatischen Retrys im Client, sondern Retry-After-Header-Informationen werden (sofern vorhanden) im Resultat zurückgegeben.
5. **Daten und Grenzen:** Es wird exakt ein Request abgesetzt. Keine Suche, keine Zweitanfrage. Disambiguationsseiten werden als solche gemeldet, ihr Text wird nicht als Definition genutzt. Ein `TargetTitleCandidate` aus den Langlinks ist explizit keine automatisch bestätigte Übersetzung. Das Datenbankschema bleibt Version 7. Weder UI, noch Cache, noch Persistenz oder Wiktionary-Fallback sind Teil dieses Pakets. Es gibt keinen `WikipediaLookupProvider`.
