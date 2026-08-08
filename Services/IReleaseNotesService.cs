namespace KnownFirst.Services;

public sealed record ReleaseNoteEntry(
    string Version,
    string TitleResourceKey,
    IReadOnlyList<string> BulletResourceKeys);

public interface IReleaseNotesService
{
    ReleaseNoteEntry? GetUnseenReleaseNotes();

    void MarkSeen(string version);

    IReadOnlyList<ReleaseNoteEntry> GetReleaseNoteHistory();
}
