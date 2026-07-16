using KnownFirst.Core.Learning;
using SQLite;

namespace KnownFirst.Data.Entities;

[Table("LearningSessionCards")]
public sealed class LearningSessionCardEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed("IX_LearningSessionCards_Session_Order", 1, Unique = true)]
    public int SessionId { get; set; }

    [Indexed]
    public int CardId { get; set; }

    [Indexed("IX_LearningSessionCards_Session_Order", 2, Unique = true)]
    public int QueueOrder { get; set; }

    public bool IsDueCard { get; set; }

    public bool IsAgainRepeat { get; set; }

    public bool AnswerRevealed { get; set; }

    public bool SpellingChecked { get; set; }

    public bool SpellingCorrect { get; set; }

    public bool IsCompleted { get; set; }

    public ReviewRating? Rating { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
