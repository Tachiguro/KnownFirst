using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

/// <summary>
/// Static cross-language conformance vectors generated on 2026-08-28 by Python 3.14.2
/// executing the unmodified open-spaced-repetition/py-fsrs v6.3.2 checkout at commit
/// 9446cb06605c597a063aeee49f7d188d42e34dc2, reference file fsrs/scheduler.py.
/// Configuration: the exact production 21 weights, desired retention 0.90, learning and
/// relearning steps [10 minutes], maximum interval 36,500 days, and fuzzing disabled.
/// The pinned upstream implementation is used as a reproducible implementation oracle;
/// it is not claimed to be a formal scientific standard.
/// </summary>
internal static class Fsrs6OracleVectors
{
    internal static readonly string UpstreamProject = "open-spaced-repetition/py-fsrs";
    internal static readonly string UpstreamVersion = "v6.3.2";
    internal static readonly string UpstreamCommit = "9446cb06605c597a063aeee49f7d188d42e34dc2";
    internal static readonly string ReferenceFile = "fsrs/scheduler.py";
    internal static readonly double DesiredRetention = 0.90;
    internal static readonly int MaximumIntervalDays = 36_500;
    internal static readonly bool FuzzEnabled = false;
    internal static IReadOnlyList<int> LearningStepsMinutes { get; } = [10];
    internal static IReadOnlyList<int> RelearningStepsMinutes { get; } = [10];
    internal static IReadOnlyList<double> Parameters { get; } =
    [
        0.212,
        1.2931,
        2.3065,
        8.2956,
        6.4133,
        0.8334,
        3.0194,
        0.001,
        1.8722,
        0.1666,
        0.796,
        1.4835,
        0.0614,
        0.2629,
        1.6483,
        0.6014,
        1.8729,
        0.5425,
        0.0912,
        0.0658,
        0.1542
    ];

    internal static IReadOnlyList<OracleHistory> All { get; } =
    [
        new("initial-again",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Learning(0.212, 6.4133, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 10))),
            ]),
        new("initial-hard",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Learning(1.2931, 5.112170705601056, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 15))),
            ]),
        new("initial-good",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(2.3065, 2.118103970459016, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 3, 12, 0))),
            ]),
        new("initial-easy",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(8.2956, 1.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 9, 12, 0))),
            ]),
        new("new-again-learning-good",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Learning(0.212, 6.4133, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 10))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Good),
                    Fsrs6Card.Review(0.24668918777567272, 6.402115069296838, Utc(2026, 1, 1, 12, 5), Utc(2026, 1, 2, 12, 5))),
            ]),
        new("new-hard-learning-hard",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Learning(1.2931, 5.112170705601056, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 15))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Hard),
                    Fsrs6Card.Learning(1.2931, 6.7404595108297, Utc(2026, 1, 1, 12, 5), dueAtUtc: Utc(2026, 1, 1, 12, 20))),
            ]),
        new("repeated-again",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Learning(0.212, 6.4133, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 10))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 1),
                        ReviewRating.Again),
                    Fsrs6Card.Learning(0.08335671711031604, 8.806304468856837, Utc(2026, 1, 1, 12, 1), dueAtUtc: Utc(2026, 1, 1, 12, 11))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 2),
                        ReviewRating.Again),
                    Fsrs6Card.Learning(0.03485140985964798, 9.592868765339693, Utc(2026, 1, 1, 12, 2), dueAtUtc: Utc(2026, 1, 1, 12, 12))),
            ]),
        new("same-day-hard-successful-recall",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 13, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(10.0, 6.665995369296838, Utc(2026, 1, 1, 13, 0), Utc(2026, 1, 11, 13, 0))),
            ]),
        new("learning-good-graduation",
            Fsrs6Card.Learning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Good),
                    Fsrs6Card.Review(2.5, 4.9902283692968386, Utc(2026, 1, 1, 12, 5), Utc(2026, 1, 3, 12, 5))),
            ]),
        new("learning-easy-graduation",
            Fsrs6Card.Learning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(4.254489599501837, 3.3144613692968385, Utc(2026, 1, 1, 12, 5), Utc(2026, 1, 5, 12, 5))),
            ]),
        new("relearning-same-day-again",
            Fsrs6Card.Relearning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(0.8356668933068423, 8.341762369296838, Utc(2026, 1, 1, 12, 5), dueAtUtc: Utc(2026, 1, 1, 12, 15))),
            ]),
        new("relearning-same-day-hard",
            Fsrs6Card.Relearning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Hard),
                    Fsrs6Card.Relearning(2.5, 6.665995369296838, Utc(2026, 1, 1, 12, 5), dueAtUtc: Utc(2026, 1, 1, 12, 20))),
            ]),
        new("relearning-same-day-good",
            Fsrs6Card.Relearning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Good),
                    Fsrs6Card.Review(2.5, 4.9902283692968386, Utc(2026, 1, 1, 12, 5), Utc(2026, 1, 3, 12, 5))),
            ]),
        new("relearning-same-day-easy",
            Fsrs6Card.Relearning(2.5, 5.0, Utc(2026, 1, 1, 12, 0), dueAtUtc: Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 5),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(4.254489599501837, 3.3144613692968385, Utc(2026, 1, 1, 12, 5), Utc(2026, 1, 5, 12, 5))),
            ]),
        new("delayed-1-days-again",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 2, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(1.208647407087057, 8.341762369296838, Utc(2026, 1, 2, 12, 0), dueAtUtc: Utc(2026, 1, 2, 12, 10))),
            ]),
        new("delayed-1-days-hard",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 2, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(11.832571652755206, 6.665995369296838, Utc(2026, 1, 2, 12, 0), Utc(2026, 1, 14, 12, 0))),
            ]),
        new("delayed-1-days-good",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 2, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(13.047176010567354, 4.9902283692968386, Utc(2026, 1, 2, 12, 0), Utc(2026, 1, 15, 12, 0))),
            ]),
        new("delayed-1-days-easy",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 2, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(15.707055950191595, 3.3144613692968385, Utc(2026, 1, 2, 12, 0), Utc(2026, 1, 18, 12, 0))),
            ]),
        new("delayed-7-days-again",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(1.3411084232733221, 8.341762369296838, Utc(2026, 1, 8, 12, 0), dueAtUtc: Utc(2026, 1, 8, 12, 10))),
            ]),
        new("delayed-7-days-hard",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(20.161495000818253, 6.665995369296838, Utc(2026, 1, 8, 12, 0), Utc(2026, 1, 28, 12, 0))),
            ]),
        new("delayed-7-days-good",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(26.896400067872044, 4.9902283692968386, Utc(2026, 1, 8, 12, 0), Utc(2026, 2, 4, 12, 0))),
            ]),
        new("delayed-7-days-easy",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(41.64526768711756, 3.3144613692968385, Utc(2026, 1, 8, 12, 0), Utc(2026, 2, 19, 12, 0))),
            ]),
        new("delayed-30-days-again",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(1.6162162872115184, 8.341762369296838, Utc(2026, 1, 31, 12, 0), dueAtUtc: Utc(2026, 1, 31, 12, 10))),
            ]),
        new("delayed-30-days-hard",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(36.19554802460354, 6.665995369296838, Utc(2026, 1, 31, 12, 0), Utc(2026, 3, 8, 12, 0))),
            ]),
        new("delayed-30-days-good",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(53.557612279021505, 4.9902283692968386, Utc(2026, 1, 31, 12, 0), Utc(2026, 3, 26, 12, 0))),
            ]),
        new("delayed-30-days-easy",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(91.57905203737938, 3.3144613692968385, Utc(2026, 1, 31, 12, 0), Utc(2026, 5, 3, 12, 0))),
            ]),
        new("delayed-365-days-again",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(2.384061247591585, 8.341762369296838, Utc(2027, 1, 1, 12, 0), dueAtUtc: Utc(2027, 1, 1, 12, 10))),
            ]),
        new("delayed-365-days-hard",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(74.61989630054178, 6.665995369296838, Utc(2027, 1, 1, 12, 0), Utc(2027, 3, 17, 12, 0))),
            ]),
        new("delayed-365-days-good",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(117.44911257156929, 4.9902283692968386, Utc(2027, 1, 1, 12, 0), Utc(2027, 4, 28, 12, 0))),
            ]),
        new("delayed-365-days-easy",
            Fsrs6Card.Review(10.0, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(211.24144293529213, 3.3144613692968385, Utc(2027, 1, 1, 12, 0), Utc(2027, 7, 31, 12, 0))),
            ]),
        new("lapse-relearning-graduation-history",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(2.3065, 2.118103970459016, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 3, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(21.41139197998198, 2.111214235785395, Utc(2026, 1, 8, 12, 0), Utc(2026, 1, 29, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(2.130867413463151, 7.392238132342694, Utc(2026, 1, 31, 12, 0), dueAtUtc: Utc(2026, 1, 31, 12, 10))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 5),
                        ReviewRating.Hard),
                    Fsrs6Card.Relearning(2.130867413463151, 8.254074519842886, Utc(2026, 1, 31, 12, 5), dueAtUtc: Utc(2026, 1, 31, 12, 20))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 20),
                        ReviewRating.Good),
                    Fsrs6Card.Review(2.130867413463151, 8.24104881461988, Utc(2026, 1, 31, 12, 20), Utc(2026, 2, 2, 12, 20))),
            ]),
        new("long-term-mixed-history",
            Fsrs6Card.New(Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(8.2956, 1.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 9, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 9, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(26.704183359371363, 4.010608969296839, Utc(2026, 1, 9, 12, 0), Utc(2026, 2, 5, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 31, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(77.20183406324921, 4.00182672962438, Utc(2026, 1, 31, 12, 0), Utc(2026, 4, 18, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 5, 1, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(371.5117353312339, 1.9827451068360853, Utc(2026, 5, 1, 12, 0), Utc(2027, 5, 8, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(6.013238244322843, 7.350011203247134, Utc(2027, 1, 1, 12, 0), dueAtUtc: Utc(2027, 1, 1, 12, 10))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2027, 1, 1, 12, 10),
                        ReviewRating.Good),
                    Fsrs6Card.Review(6.013238244322843, 7.337889561340726, Utc(2027, 1, 1, 12, 10), Utc(2027, 1, 7, 12, 10))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2028, 1, 1, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(54.0248812711473, 7.325780041076223, Utc(2028, 1, 1, 12, 0), Utc(2028, 2, 24, 12, 0))),
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2039, 9, 10, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(646.8742905330109, 6.4174087187508215, Utc(2039, 9, 10, 12, 0), Utc(2041, 6, 18, 12, 0))),
            ]),
        new("minimum-stability-and-interval-clamp",
            Fsrs6Card.Review(0.001, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 1),
                        ReviewRating.Good),
                    Fsrs6Card.Review(0.0016553397873898706, 4.9902283692968386, Utc(2026, 1, 1, 12, 1), Utc(2026, 1, 2, 12, 1))),
            ]),
        new("difficulty-near-one",
            Fsrs6Card.Review(10.0, 1.000000000001, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(62.74211281185732, 1.0, Utc(2026, 1, 8, 12, 0), Utc(2026, 3, 12, 12, 0))),
            ]),
        new("difficulty-near-ten",
            Fsrs6Card.Review(10.0, 9.999999999999, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 8, 12, 0),
                        ReviewRating.Again),
                    Fsrs6Card.Relearning(1.285229390852606, 9.98522836929651, Utc(2026, 1, 8, 12, 0), dueAtUtc: Utc(2026, 1, 8, 12, 10))),
            ]),
        new("same-day-zero-elapsed",
            Fsrs6Card.Review(2.5, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 0),
                        ReviewRating.Hard),
                    Fsrs6Card.Review(2.5, 6.665995369296838, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 3, 12, 0))),
            ]),
        new("exact-twenty-four-hour-boundary",
            Fsrs6Card.Review(2.5, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 2, 12, 0),
                        ReviewRating.Good),
                    Fsrs6Card.Review(5.881363974796532, 4.9902283692968386, Utc(2026, 1, 2, 12, 0), Utc(2026, 1, 8, 12, 0))),
            ]),
        new("maximum-interval-clamp",
            Fsrs6Card.Review(1e+100, 5.0, Utc(2026, 1, 1, 12, 0), Utc(2026, 1, 1, 12, 0)),
            [
                new(
                    new Fsrs6ReviewEvent(
                        Utc(2026, 1, 1, 12, 1),
                        ReviewRating.Easy),
                    Fsrs6Card.Review(1e+100, 3.3144613692968385, Utc(2026, 1, 1, 12, 1), Utc(2125, 12, 8, 12, 1))),
            ]),
    ];

    internal sealed record OracleHistory(
        string Name,
        Fsrs6Card InitialCard,
        IReadOnlyList<OracleStep> Steps);

    internal readonly record struct OracleStep(
        Fsrs6ReviewEvent Event,
        Fsrs6Card ExpectedCard);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
