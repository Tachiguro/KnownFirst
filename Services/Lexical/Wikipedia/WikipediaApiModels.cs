using System.Text.Json.Serialization;

namespace KnownFirst.Services.Lexical.Wikipedia;

public sealed record WikipediaApiResponse(
    [property: JsonPropertyName("error")] WikipediaApiError? Error,
    [property: JsonPropertyName("query")] WikipediaApiQuery? Query
);

public sealed record WikipediaApiError(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("info")] string? Info
);

public sealed record WikipediaApiQuery(
    [property: JsonPropertyName("normalized")] IReadOnlyList<WikipediaApiNormalized>? Normalized,
    [property: JsonPropertyName("redirects")] IReadOnlyList<WikipediaApiRedirect>? Redirects,
    [property: JsonPropertyName("pages")] IReadOnlyList<WikipediaApiPage>? Pages
);

public sealed record WikipediaApiNormalized(
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("to")] string? To
);

public sealed record WikipediaApiRedirect(
    [property: JsonPropertyName("from")] string? From,
    [property: JsonPropertyName("to")] string? To
);

public sealed record WikipediaApiPage(
    [property: JsonPropertyName("pageid")] long? PageId,
    [property: JsonPropertyName("ns")] int? Ns,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("missing")] bool? Missing,
    [property: JsonPropertyName("lastrevid")] long? LastRevId,
    [property: JsonPropertyName("canonicalurl")] string? CanonicalUrl,
    [property: JsonPropertyName("extract")] string? Extract,
    [property: JsonPropertyName("pageprops")] WikipediaApiPageProps? PageProps,
    [property: JsonPropertyName("langlinks")] IReadOnlyList<WikipediaApiLangLink>? LangLinks
);

public sealed record WikipediaApiPageProps(
    [property: JsonPropertyName("disambiguation")] string? Disambiguation
);

public sealed record WikipediaApiLangLink(
    [property: JsonPropertyName("lang")] string? Lang,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("url")] string? Url
);
