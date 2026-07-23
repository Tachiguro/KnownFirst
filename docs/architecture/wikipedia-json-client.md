# Wikipedia JSON API Client

## Kontext
KnownFirst benötigt einen Fallback-Provider für Begriffe, die im primären Provider (Wiktionary) nicht gefunden werden. Wikipedia eignet sich hierfür, ist jedoch in seiner HTML-Struktur stark variabel.

## Entscheidung
Für die Anbindung an Wikipedia wird ausschließlich die offizielle MediaWiki Action API über JSON (`formatversion=2`) verwendet. Ein `WikipediaHtmlParser` ist architektonisch untersagt.

## Begründung
Die HTML-Struktur von Wikipedia-Artikeln ändert sich häufig und ist extrem komplex (Infoboxen, Navboxen, verschiedene Tabellenlayouts). Ein HTML-Scraping wäre fehleranfällig und wartungsintensiv. Die MediaWiki JSON-API bietet standardisierte Felder (`extract`, `pageprops.disambiguation`, `langlinks`), die robust maschinenlesbar sind.

## Implementierungsdetails
1. **Source Generation:** Die Deserialisierung der JSON-Antworten erfolgt ausschließlich über `System.Text.Json` mit Source Generation (`JsonSourceGenerationOptions`), um AOT-Kompatibilität und Trimming-Sicherheit für Android-Release-Builds zu gewährleisten. Reflektion ist vollständig verboten.
2. **Deterministische Tests:** Die Testabdeckung wird ausschließlich durch lokale JSON-Fixtures erzeugt, welche die Struktur der echten Wikipedia-API abbilden. Es gibt keine Live-Netzwerktests.
3. **Fehlerbehandlung:** HTTP-Fehler, Timeouts und Rate-Limits (HTTP 429) werden im Client abgefangen und gemappt. HTTP Timeouts und vorübergehende HTTP-Fehler werden auf einen `TransientFailure` Result-Status abgebildet, während permanente HTTP-Fehler oder JSON Parse-Fehler auf ihre spezifischen Result-Stati abgebildet werden (anstatt Exceptions zu werfen). Eine durch einen internen Client-Timeout ausgelöste Cancellation (`OperationCanceledException`) wird sicher gefangen und als vorübergehender Fehler (`TransientFailure`) auf Provider-Ebene abgebildet, um eine geleakte Exception zu vermeiden. Aufrufer-Cancellation wird normal weitergereicht. Es gibt keine automatischen Retrys im Client, sondern Retry-After-Header-Informationen werden im Resultat zurückgegeben.

### Original client package boundary

When the low-level client package was introduced:
- it implemented only `IWikipediaApiClient`;
- no provider, cache orchestration, or fallback was included in that original package;
- these statements describe historical scope only.

### Current verified architecture

On open PR #11:
- `WikipediaLookupProvider` implements `ILexicalLookupProvider`;
- it is registered through `LexicalLookupProviderResolver`;
- `LexicalEnrichmentService` may create exactly one Wikipedia fallback request after a complete final Wiktionary `NotFound`;
- `Definition` and `DefinitionAndTranslation` are eligible;
- `Translation`-only is not eligible;
- operational failures and cancellation do not trigger fallback;
- `LexicalCacheRepository` stores results using provider-specific identity and schema version;
- no UI or migration is included;
- `PRAGMA user_version` remains 7;
- PR #11 is open and unmerged, so this is not yet stable master behavior.

## Verified boundaries

- The client sends exactly one MediaWiki Action API request per lookup.
- The Search module (`list=search`) is not used.
- JSON DTOs are deserialized with source-generated `WikipediaJsonSerializerContext` metadata.
- Redirect chains are resolved from the single API response and keep the first redirect source as `RedirectedFrom`.
- `Retry-After` supports both delta seconds and absolute HTTP-date values.
- Disambiguation pages return `Disambiguation` without usable extract content.
- `TargetTitleCandidate` is not a translation.
- `MediaWiki Action API JSON` only; no HTML parser is used.
- Caller cancellation propagation is fully implemented.
