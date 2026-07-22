namespace KnownFirst.Services.Lexical.Wikipedia;

public interface IWikipediaApiClient
{
    Task<WikipediaArticleResult> GetArticleAsync(
        WikipediaArticleRequest request,
        CancellationToken cancellationToken = default);
}
