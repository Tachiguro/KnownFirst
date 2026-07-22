using KnownFirst.Core.Learning;
using KnownFirst.Core.Preparation;
using KnownFirst.Core.Text;
using KnownFirst.Models;
using KnownFirst.Models.Backup;

namespace KnownFirst.Services.DataSafety;

public static class BackupEnumMappings
{
    public static BackupKnowledgeState ToBackup(WordStatus value) => value switch
    {
        WordStatus.Unreviewed => BackupKnowledgeState.Unreviewed,
        WordStatus.Known => BackupKnowledgeState.Known,
        WordStatus.UnknownBacklog => BackupKnowledgeState.UnknownBacklog,
        WordStatus.Prepared => BackupKnowledgeState.Prepared,
        WordStatus.Learning => BackupKnowledgeState.Learning,
        WordStatus.Mastered => BackupKnowledgeState.Mastered,
        WordStatus.Ignored => BackupKnowledgeState.Ignored,
        _ => throw UnknownEnum()
    };

    public static WordStatus ToPersistence(BackupKnowledgeState value) => value switch
    {
        BackupKnowledgeState.Unreviewed => WordStatus.Unreviewed,
        BackupKnowledgeState.Known => WordStatus.Known,
        BackupKnowledgeState.UnknownBacklog => WordStatus.UnknownBacklog,
        BackupKnowledgeState.Prepared => WordStatus.Prepared,
        BackupKnowledgeState.Learning => WordStatus.Learning,
        BackupKnowledgeState.Mastered => WordStatus.Mastered,
        BackupKnowledgeState.Ignored => WordStatus.Ignored,
        _ => throw UnknownEnum()
    };

    public static BackupTokenKind ToBackup(TokenKind value) => value switch
    {
        TokenKind.Word => BackupTokenKind.Word,
        TokenKind.Acronym => BackupTokenKind.Acronym,
        TokenKind.Abbreviation => BackupTokenKind.Abbreviation,
        TokenKind.TechnicalTerm => BackupTokenKind.TechnicalTerm,
        _ => throw UnknownEnum()
    };

    public static TokenKind ToPersistence(BackupTokenKind value) => value switch
    {
        BackupTokenKind.Word => TokenKind.Word,
        BackupTokenKind.Acronym => TokenKind.Acronym,
        BackupTokenKind.Abbreviation => TokenKind.Abbreviation,
        BackupTokenKind.TechnicalTerm => TokenKind.TechnicalTerm,
        _ => throw UnknownEnum()
    };

    public static BackupPreparationState ToBackup(PreparationState value) => value switch
    {
        PreparationState.Unprepared => BackupPreparationState.Unprepared,
        PreparationState.Preparing => BackupPreparationState.Preparing,
        PreparationState.Prepared => BackupPreparationState.Prepared,
        PreparationState.PreparationFailed => BackupPreparationState.PreparationFailed,
        _ => throw UnknownEnum()
    };

    public static PreparationState ToPersistence(BackupPreparationState value) => value switch
    {
        BackupPreparationState.Unprepared => PreparationState.Unprepared,
        BackupPreparationState.Preparing => PreparationState.Preparing,
        BackupPreparationState.Prepared => PreparationState.Prepared,
        BackupPreparationState.PreparationFailed => PreparationState.PreparationFailed,
        _ => throw UnknownEnum()
    };

    public static BackupLearningInteractionMode ToBackup(LearningInteractionMode value) => value switch
    {
        LearningInteractionMode.Reading => BackupLearningInteractionMode.Reading,
        LearningInteractionMode.Typing => BackupLearningInteractionMode.Typing,
        _ => throw UnknownEnum()
    };

    public static LearningInteractionMode ToPersistence(BackupLearningInteractionMode value) => value switch
    {
        BackupLearningInteractionMode.Reading => LearningInteractionMode.Reading,
        BackupLearningInteractionMode.Typing => LearningInteractionMode.Typing,
        _ => throw UnknownEnum()
    };

    public static BackupTechnicalTokenFamily ToBackup(TechnicalTokenFamily value) => value switch
    {
        TechnicalTokenFamily.None => BackupTechnicalTokenFamily.None,
        TechnicalTokenFamily.Cve => BackupTechnicalTokenFamily.Cve,
        TechnicalTokenFamily.Sha => BackupTechnicalTokenFamily.Sha,
        _ => throw UnknownEnum()
    };

    public static TechnicalTokenFamily ToPersistence(BackupTechnicalTokenFamily value) => value switch
    {
        BackupTechnicalTokenFamily.None => TechnicalTokenFamily.None,
        BackupTechnicalTokenFamily.Cve => TechnicalTokenFamily.Cve,
        BackupTechnicalTokenFamily.Sha => TechnicalTokenFamily.Sha,
        _ => throw UnknownEnum()
    };

    public static BackupLexicalLookupMode ToBackup(LexicalLookupMode value) => value switch
    {
        LexicalLookupMode.Definition => BackupLexicalLookupMode.Definition,
        LexicalLookupMode.Translation => BackupLexicalLookupMode.Translation,
        LexicalLookupMode.DefinitionAndTranslation => BackupLexicalLookupMode.DefinitionAndTranslation,
        _ => throw UnknownEnum()
    };

    public static LexicalLookupMode ToPersistence(BackupLexicalLookupMode value) => value switch
    {
        BackupLexicalLookupMode.Definition => LexicalLookupMode.Definition,
        BackupLexicalLookupMode.Translation => LexicalLookupMode.Translation,
        BackupLexicalLookupMode.DefinitionAndTranslation => LexicalLookupMode.DefinitionAndTranslation,
        _ => throw UnknownEnum()
    };

    public static BackupReviewSessionStatus ToBackup(ReviewSessionStatus value) => value switch
    {
        ReviewSessionStatus.Active => BackupReviewSessionStatus.Active,
        ReviewSessionStatus.Completed => BackupReviewSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static ReviewSessionStatus ToPersistence(BackupReviewSessionStatus value) => value switch
    {
        BackupReviewSessionStatus.Active => ReviewSessionStatus.Active,
        BackupReviewSessionStatus.Completed => ReviewSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static BackupPreparationMethod ToBackup(PreparationMethod value) => value switch
    {
        PreparationMethod.AutomaticOnline => BackupPreparationMethod.AutomaticOnline,
        PreparationMethod.Manual => BackupPreparationMethod.Manual,
        _ => throw UnknownEnum()
    };

    public static PreparationMethod ToPersistence(BackupPreparationMethod value) => value switch
    {
        BackupPreparationMethod.AutomaticOnline => PreparationMethod.AutomaticOnline,
        BackupPreparationMethod.Manual => PreparationMethod.Manual,
        _ => throw UnknownEnum()
    };

    public static BackupPreparationSessionStatus ToBackup(PreparationSessionStatus value) => value switch
    {
        PreparationSessionStatus.Active => BackupPreparationSessionStatus.Active,
        PreparationSessionStatus.Completed => BackupPreparationSessionStatus.Completed,
        PreparationSessionStatus.Cancelled => BackupPreparationSessionStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static PreparationSessionStatus ToPersistence(BackupPreparationSessionStatus value) => value switch
    {
        BackupPreparationSessionStatus.Active => PreparationSessionStatus.Active,
        BackupPreparationSessionStatus.Completed => PreparationSessionStatus.Completed,
        BackupPreparationSessionStatus.Cancelled => PreparationSessionStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static BackupPreparationCandidateStatus ToBackup(PreparationCandidateStatus value) => value switch
    {
        PreparationCandidateStatus.Pending => BackupPreparationCandidateStatus.Pending,
        PreparationCandidateStatus.ResultReady => BackupPreparationCandidateStatus.ResultReady,
        PreparationCandidateStatus.Prepared => BackupPreparationCandidateStatus.Prepared,
        PreparationCandidateStatus.Skipped => BackupPreparationCandidateStatus.Skipped,
        PreparationCandidateStatus.Failed => BackupPreparationCandidateStatus.Failed,
        PreparationCandidateStatus.MarkedKnown => BackupPreparationCandidateStatus.MarkedKnown,
        PreparationCandidateStatus.Excluded => BackupPreparationCandidateStatus.Excluded,
        PreparationCandidateStatus.Cancelled => BackupPreparationCandidateStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static PreparationCandidateStatus ToPersistence(BackupPreparationCandidateStatus value) => value switch
    {
        BackupPreparationCandidateStatus.Pending => PreparationCandidateStatus.Pending,
        BackupPreparationCandidateStatus.ResultReady => PreparationCandidateStatus.ResultReady,
        BackupPreparationCandidateStatus.Prepared => PreparationCandidateStatus.Prepared,
        BackupPreparationCandidateStatus.Skipped => PreparationCandidateStatus.Skipped,
        BackupPreparationCandidateStatus.Failed => PreparationCandidateStatus.Failed,
        BackupPreparationCandidateStatus.MarkedKnown => PreparationCandidateStatus.MarkedKnown,
        BackupPreparationCandidateStatus.Excluded => PreparationCandidateStatus.Excluded,
        BackupPreparationCandidateStatus.Cancelled => PreparationCandidateStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static BackupCardDirection ToBackup(CardDirection value) => value switch
    {
        CardDirection.TermToMeaning => BackupCardDirection.TermToMeaning,
        CardDirection.MeaningToTerm => BackupCardDirection.MeaningToTerm,
        _ => throw UnknownEnum()
    };

    public static CardDirection ToPersistence(BackupCardDirection value) => value switch
    {
        BackupCardDirection.TermToMeaning => CardDirection.TermToMeaning,
        BackupCardDirection.MeaningToTerm => CardDirection.MeaningToTerm,
        _ => throw UnknownEnum()
    };

    public static BackupCardState ToBackup(CardState value) => value switch
    {
        CardState.New => BackupCardState.New,
        CardState.Learning => BackupCardState.Learning,
        CardState.Review => BackupCardState.Review,
        CardState.Relearning => BackupCardState.Relearning,
        CardState.Suspended => BackupCardState.Suspended,
        CardState.Retired => BackupCardState.Retired,
        _ => throw UnknownEnum()
    };

    public static CardState ToPersistence(BackupCardState value) => value switch
    {
        BackupCardState.New => CardState.New,
        BackupCardState.Learning => CardState.Learning,
        BackupCardState.Review => CardState.Review,
        BackupCardState.Relearning => CardState.Relearning,
        BackupCardState.Suspended => CardState.Suspended,
        BackupCardState.Retired => CardState.Retired,
        _ => throw UnknownEnum()
    };

    public static BackupReviewRating ToBackup(ReviewRating value) => value switch
    {
        ReviewRating.Again => BackupReviewRating.Again,
        ReviewRating.Hard => BackupReviewRating.Hard,
        ReviewRating.Good => BackupReviewRating.Good,
        ReviewRating.Easy => BackupReviewRating.Easy,
        _ => throw UnknownEnum()
    };

    public static ReviewRating ToPersistence(BackupReviewRating value) => value switch
    {
        BackupReviewRating.Again => ReviewRating.Again,
        BackupReviewRating.Hard => ReviewRating.Hard,
        BackupReviewRating.Good => ReviewRating.Good,
        BackupReviewRating.Easy => ReviewRating.Easy,
        _ => throw UnknownEnum()
    };

    public static BackupLearningSessionStatus ToBackup(LearningSessionStatus value) => value switch
    {
        LearningSessionStatus.Active => BackupLearningSessionStatus.Active,
        LearningSessionStatus.Completed => BackupLearningSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static LearningSessionStatus ToPersistence(BackupLearningSessionStatus value) => value switch
    {
        BackupLearningSessionStatus.Active => LearningSessionStatus.Active,
        BackupLearningSessionStatus.Completed => LearningSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static BackupLexicalLookupStatus ToBackup(LexicalLookupStatus value) => value switch
    {
        LexicalLookupStatus.Success => BackupLexicalLookupStatus.Success,
        LexicalLookupStatus.NotFound => BackupLexicalLookupStatus.NotFound,
        LexicalLookupStatus.TransientFailure => BackupLexicalLookupStatus.TransientFailure,
        LexicalLookupStatus.PermanentFailure => BackupLexicalLookupStatus.PermanentFailure,
        LexicalLookupStatus.ParseFailure => BackupLexicalLookupStatus.ParseFailure,
        _ => throw UnknownEnum()
    };

    public static LexicalLookupStatus ToPersistence(BackupLexicalLookupStatus value) => value switch
    {
        BackupLexicalLookupStatus.Success => LexicalLookupStatus.Success,
        BackupLexicalLookupStatus.NotFound => LexicalLookupStatus.NotFound,
        BackupLexicalLookupStatus.TransientFailure => LexicalLookupStatus.TransientFailure,
        BackupLexicalLookupStatus.PermanentFailure => LexicalLookupStatus.PermanentFailure,
        BackupLexicalLookupStatus.ParseFailure => LexicalLookupStatus.ParseFailure,
        _ => throw UnknownEnum()
    };

    public static BackupGrammaticalRelationKind ToBackup(GrammaticalRelationKind value) => value switch
    {
        GrammaticalRelationKind.Plural => BackupGrammaticalRelationKind.Plural,
        GrammaticalRelationKind.Singular => BackupGrammaticalRelationKind.Singular,
        GrammaticalRelationKind.ThirdPersonSingular => BackupGrammaticalRelationKind.ThirdPersonSingular,
        GrammaticalRelationKind.PastTense => BackupGrammaticalRelationKind.PastTense,
        GrammaticalRelationKind.PastParticiple => BackupGrammaticalRelationKind.PastParticiple,
        GrammaticalRelationKind.PresentParticiple => BackupGrammaticalRelationKind.PresentParticiple,
        GrammaticalRelationKind.Comparative => BackupGrammaticalRelationKind.Comparative,
        GrammaticalRelationKind.Superlative => BackupGrammaticalRelationKind.Superlative,
        _ => throw UnknownEnum()
    };

    public static GrammaticalRelationKind ToPersistence(BackupGrammaticalRelationKind value) => value switch
    {
        BackupGrammaticalRelationKind.Plural => GrammaticalRelationKind.Plural,
        BackupGrammaticalRelationKind.Singular => GrammaticalRelationKind.Singular,
        BackupGrammaticalRelationKind.ThirdPersonSingular => GrammaticalRelationKind.ThirdPersonSingular,
        BackupGrammaticalRelationKind.PastTense => GrammaticalRelationKind.PastTense,
        BackupGrammaticalRelationKind.PastParticiple => GrammaticalRelationKind.PastParticiple,
        BackupGrammaticalRelationKind.PresentParticiple => GrammaticalRelationKind.PresentParticiple,
        BackupGrammaticalRelationKind.Comparative => GrammaticalRelationKind.Comparative,
        BackupGrammaticalRelationKind.Superlative => GrammaticalRelationKind.Superlative,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupSourcePlatform value) => value switch
    {
        BackupSourcePlatform.Windows => "windows",
        BackupSourcePlatform.Android => "android",
        _ => throw UnknownEnum()
    };

    public static BackupSourcePlatform ParseSourcePlatform(string value) => value switch
    {
        "windows" => BackupSourcePlatform.Windows,
        "android" => BackupSourcePlatform.Android,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupLexicalLookupMode value) => value switch
    {
        BackupLexicalLookupMode.Definition => "definition",
        BackupLexicalLookupMode.Translation => "translation",
        BackupLexicalLookupMode.DefinitionAndTranslation => "definition-and-translation",
        _ => throw UnknownEnum()
    };

    public static BackupLexicalLookupMode ParseLexicalLookupMode(string value) => value switch
    {
        "definition" => BackupLexicalLookupMode.Definition,
        "translation" => BackupLexicalLookupMode.Translation,
        "definition-and-translation" => BackupLexicalLookupMode.DefinitionAndTranslation,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupKnowledgeState value) => value switch
    {
        BackupKnowledgeState.Unreviewed => "unreviewed",
        BackupKnowledgeState.Known => "known",
        BackupKnowledgeState.UnknownBacklog => "unknown-backlog",
        BackupKnowledgeState.Prepared => "prepared",
        BackupKnowledgeState.Learning => "learning",
        BackupKnowledgeState.Mastered => "mastered",
        BackupKnowledgeState.Ignored => "ignored",
        _ => throw UnknownEnum()
    };

    public static BackupKnowledgeState ParseKnowledgeState(string value) => value switch
    {
        "unreviewed" => BackupKnowledgeState.Unreviewed,
        "known" => BackupKnowledgeState.Known,
        "unknown-backlog" => BackupKnowledgeState.UnknownBacklog,
        "prepared" => BackupKnowledgeState.Prepared,
        "learning" => BackupKnowledgeState.Learning,
        "mastered" => BackupKnowledgeState.Mastered,
        "ignored" => BackupKnowledgeState.Ignored,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupTokenKind value) => value switch
    {
        BackupTokenKind.Word => "word",
        BackupTokenKind.Acronym => "acronym",
        BackupTokenKind.Abbreviation => "abbreviation",
        BackupTokenKind.TechnicalTerm => "technical-term",
        _ => throw UnknownEnum()
    };

    public static BackupTokenKind ParseTokenKind(string value) => value switch
    {
        "word" => BackupTokenKind.Word,
        "acronym" => BackupTokenKind.Acronym,
        "abbreviation" => BackupTokenKind.Abbreviation,
        "technical-term" => BackupTokenKind.TechnicalTerm,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupPreparationState value) => value switch
    {
        BackupPreparationState.Unprepared => "unprepared",
        BackupPreparationState.Preparing => "preparing",
        BackupPreparationState.Prepared => "prepared",
        BackupPreparationState.PreparationFailed => "preparation-failed",
        _ => throw UnknownEnum()
    };

    public static BackupPreparationState ParsePreparationState(string value) => value switch
    {
        "unprepared" => BackupPreparationState.Unprepared,
        "preparing" => BackupPreparationState.Preparing,
        "prepared" => BackupPreparationState.Prepared,
        "preparation-failed" => BackupPreparationState.PreparationFailed,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupLearningInteractionMode value) => value switch
    {
        BackupLearningInteractionMode.Reading => "reading",
        BackupLearningInteractionMode.Typing => "typing",
        _ => throw UnknownEnum()
    };

    public static BackupLearningInteractionMode ParseLearningInteractionMode(string value) => value switch
    {
        "reading" => BackupLearningInteractionMode.Reading,
        "typing" => BackupLearningInteractionMode.Typing,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupTechnicalTokenFamily value) => value switch
    {
        BackupTechnicalTokenFamily.None => "none",
        BackupTechnicalTokenFamily.Cve => "cve",
        BackupTechnicalTokenFamily.Sha => "sha",
        _ => throw UnknownEnum()
    };

    public static BackupTechnicalTokenFamily ParseTechnicalTokenFamily(string value) => value switch
    {
        "none" => BackupTechnicalTokenFamily.None,
        "cve" => BackupTechnicalTokenFamily.Cve,
        "sha" => BackupTechnicalTokenFamily.Sha,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupCardDirection value) => value switch
    {
        BackupCardDirection.TermToMeaning => "term-to-meaning",
        BackupCardDirection.MeaningToTerm => "meaning-to-term",
        _ => throw UnknownEnum()
    };

    public static BackupCardDirection ParseCardDirection(string value) => value switch
    {
        "term-to-meaning" => BackupCardDirection.TermToMeaning,
        "meaning-to-term" => BackupCardDirection.MeaningToTerm,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupCardState value) => value switch
    {
        BackupCardState.New => "new",
        BackupCardState.Learning => "learning",
        BackupCardState.Review => "review",
        BackupCardState.Relearning => "relearning",
        BackupCardState.Suspended => "suspended",
        BackupCardState.Retired => "retired",
        _ => throw UnknownEnum()
    };

    public static BackupCardState ParseCardState(string value) => value switch
    {
        "new" => BackupCardState.New,
        "learning" => BackupCardState.Learning,
        "review" => BackupCardState.Review,
        "relearning" => BackupCardState.Relearning,
        "suspended" => BackupCardState.Suspended,
        "retired" => BackupCardState.Retired,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupReviewRating value) => value switch
    {
        BackupReviewRating.Again => "again",
        BackupReviewRating.Hard => "hard",
        BackupReviewRating.Good => "good",
        BackupReviewRating.Easy => "easy",
        _ => throw UnknownEnum()
    };

    public static BackupReviewRating ParseReviewRating(string value) => value switch
    {
        "again" => BackupReviewRating.Again,
        "hard" => BackupReviewRating.Hard,
        "good" => BackupReviewRating.Good,
        "easy" => BackupReviewRating.Easy,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupReviewSessionStatus value) => value switch
    {
        BackupReviewSessionStatus.Active => "active",
        BackupReviewSessionStatus.Completed => "completed",
        _ => throw UnknownEnum()
    };

    public static BackupReviewSessionStatus ParseReviewSessionStatus(string value) => value switch
    {
        "active" => BackupReviewSessionStatus.Active,
        "completed" => BackupReviewSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupPreparationMethod value) => value switch
    {
        BackupPreparationMethod.AutomaticOnline => "automatic-online",
        BackupPreparationMethod.Manual => "manual",
        _ => throw UnknownEnum()
    };

    public static BackupPreparationMethod ParsePreparationMethod(string value) => value switch
    {
        "automatic-online" => BackupPreparationMethod.AutomaticOnline,
        "manual" => BackupPreparationMethod.Manual,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupPreparationSessionStatus value) => value switch
    {
        BackupPreparationSessionStatus.Active => "active",
        BackupPreparationSessionStatus.Completed => "completed",
        BackupPreparationSessionStatus.Cancelled => "cancelled",
        _ => throw UnknownEnum()
    };

    public static BackupPreparationSessionStatus ParsePreparationSessionStatus(string value) => value switch
    {
        "active" => BackupPreparationSessionStatus.Active,
        "completed" => BackupPreparationSessionStatus.Completed,
        "cancelled" => BackupPreparationSessionStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupPreparationCandidateStatus value) => value switch
    {
        BackupPreparationCandidateStatus.Pending => "pending",
        BackupPreparationCandidateStatus.ResultReady => "result-ready",
        BackupPreparationCandidateStatus.Prepared => "prepared",
        BackupPreparationCandidateStatus.Skipped => "skipped",
        BackupPreparationCandidateStatus.Failed => "failed",
        BackupPreparationCandidateStatus.MarkedKnown => "marked-known",
        BackupPreparationCandidateStatus.Excluded => "excluded",
        BackupPreparationCandidateStatus.Cancelled => "cancelled",
        _ => throw UnknownEnum()
    };

    public static BackupPreparationCandidateStatus ParsePreparationCandidateStatus(string value) => value switch
    {
        "pending" => BackupPreparationCandidateStatus.Pending,
        "result-ready" => BackupPreparationCandidateStatus.ResultReady,
        "prepared" => BackupPreparationCandidateStatus.Prepared,
        "skipped" => BackupPreparationCandidateStatus.Skipped,
        "failed" => BackupPreparationCandidateStatus.Failed,
        "marked-known" => BackupPreparationCandidateStatus.MarkedKnown,
        "excluded" => BackupPreparationCandidateStatus.Excluded,
        "cancelled" => BackupPreparationCandidateStatus.Cancelled,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupLearningSessionStatus value) => value switch
    {
        BackupLearningSessionStatus.Active => "active",
        BackupLearningSessionStatus.Completed => "completed",
        _ => throw UnknownEnum()
    };

    public static BackupLearningSessionStatus ParseLearningSessionStatus(string value) => value switch
    {
        "active" => BackupLearningSessionStatus.Active,
        "completed" => BackupLearningSessionStatus.Completed,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupLexicalLookupStatus value) => value switch
    {
        BackupLexicalLookupStatus.Success => "success",
        BackupLexicalLookupStatus.NotFound => "not-found",
        BackupLexicalLookupStatus.TransientFailure => "transient-failure",
        BackupLexicalLookupStatus.PermanentFailure => "permanent-failure",
        BackupLexicalLookupStatus.ParseFailure => "parse-failure",
        _ => throw UnknownEnum()
    };

    public static BackupLexicalLookupStatus ParseLexicalLookupStatus(string value) => value switch
    {
        "success" => BackupLexicalLookupStatus.Success,
        "not-found" => BackupLexicalLookupStatus.NotFound,
        "transient-failure" => BackupLexicalLookupStatus.TransientFailure,
        "permanent-failure" => BackupLexicalLookupStatus.PermanentFailure,
        "parse-failure" => BackupLexicalLookupStatus.ParseFailure,
        _ => throw UnknownEnum()
    };

    public static string ToExternalString(BackupGrammaticalRelationKind value) => value switch
    {
        BackupGrammaticalRelationKind.Plural => "plural",
        BackupGrammaticalRelationKind.Singular => "singular",
        BackupGrammaticalRelationKind.ThirdPersonSingular => "third-person-singular",
        BackupGrammaticalRelationKind.PastTense => "past-tense",
        BackupGrammaticalRelationKind.PastParticiple => "past-participle",
        BackupGrammaticalRelationKind.PresentParticiple => "present-participle",
        BackupGrammaticalRelationKind.Comparative => "comparative",
        BackupGrammaticalRelationKind.Superlative => "superlative",
        _ => throw UnknownEnum()
    };

    public static BackupGrammaticalRelationKind ParseGrammaticalRelationKind(string value) => value switch
    {
        "plural" => BackupGrammaticalRelationKind.Plural,
        "singular" => BackupGrammaticalRelationKind.Singular,
        "third-person-singular" => BackupGrammaticalRelationKind.ThirdPersonSingular,
        "past-tense" => BackupGrammaticalRelationKind.PastTense,
        "past-participle" => BackupGrammaticalRelationKind.PastParticiple,
        "present-participle" => BackupGrammaticalRelationKind.PresentParticiple,
        "comparative" => BackupGrammaticalRelationKind.Comparative,
        "superlative" => BackupGrammaticalRelationKind.Superlative,
        _ => throw UnknownEnum()
    };

    private static BackupFormatException UnknownEnum() =>
        new(BackupErrorCodes.UnknownEnum);
}
