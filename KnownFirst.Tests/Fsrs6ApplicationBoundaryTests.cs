using System.Reflection;
using KnownFirst.Application.Learning;
using KnownFirst.Core.Learning;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6ApplicationBoundaryTests
{
    private static readonly DateTimeOffset StartUtc = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

    private sealed class LocalFakeClock : IClock
    {
        public DateTime UtcNow { get; set; }

        public LocalFakeClock(DateTime utcNow)
        {
            UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }
    }

    private sealed class BadKindClock(DateTime value) : IClock
    {
        public DateTime UtcNow => value;
    }

    [TestMethod]
    public void Architecture_KnownFirstApplication_DoesNotReferencePersistenceOrUi()
    {
        var applicationAssembly = typeof(Fsrs6ScheduleProjection).Assembly;
        var referencedAssemblies = applicationAssembly.GetReferencedAssemblies();

        var forbiddenNames = new[] { "sqlite", "Data", "Maui", "Microsoft.Maui", "KnownFirst.csproj" };

        foreach (var referenced in referencedAssemblies)
        {
            foreach (var forbidden in forbiddenNames)
            {
                Assert.IsFalse(
                    referenced.Name!.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"KnownFirst.Application must not reference {referenced.Name}.");
            }
        }

        // Must reference KnownFirst.Core
        Assert.IsTrue(
            referencedAssemblies.Any(a => a.Name == "KnownFirst.Core"),
            "KnownFirst.Application must reference KnownFirst.Core.");
    }

    [TestMethod]
    public void Architecture_KnownFirstApplication_DoesNotExposeLegacyOrPersistenceConcepts()
    {
        var applicationAssembly = typeof(Fsrs6ScheduleProjection).Assembly;
        var exportedTypes = applicationAssembly.GetExportedTypes();

        var forbiddenTypeSubstrings = new[]
        {
            "CardSchedule",
            "Mastered",
            "Retired",
            "Suspended",
            "AlreadyKnown",
            "StopLearning",
            "Entity",
            "Repository"
        };

        foreach (var type in exportedTypes)
        {
            foreach (var forbidden in forbiddenTypeSubstrings)
            {
                Assert.IsFalse(
                    type.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"KnownFirst.Application must not expose type {type.Name}.");
            }
        }

        var projectionProperties = typeof(Fsrs6ScheduleProjection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        var forbiddenProperties = new[]
        {
            "IntervalDays",
            "EaseFactor",
            "Mastered",
            "Retired",
            "Suspended",
            "AlreadyKnown",
            "StopLearning",
            "Id",
            "CardId",
            "WordId"
        };

        foreach (var forbidden in forbiddenProperties)
        {
            CollectionAssert.DoesNotContain(
                projectionProperties,
                forbidden,
                $"Fsrs6ScheduleProjection must not contain property {forbidden}.");
        }
    }

    [TestMethod]
    public void Projection_AllFourStates_RoundTripCorrectlyWithCoreCard()
    {
        // New
        var newProj = Fsrs6ScheduleProjection.New(StartUtc.AddDays(1));
        var newCard = newProj.ToCard();
        Assert.AreEqual(Fsrs6CardState.New, newCard.State);
        Assert.IsNull(newCard.Stability);
        Assert.IsNull(newCard.Difficulty);
        Assert.IsNull(newCard.LastReviewedAtUtc);
        Assert.IsNull(newCard.StepIndex);
        Assert.AreEqual(StartUtc.AddDays(1), newCard.DueAtUtc);

        var newRoundTrip = Fsrs6ScheduleProjection.FromCard(newCard);
        Assert.AreEqual(newProj, newRoundTrip);

        // Learning
        var learningProj = Fsrs6ScheduleProjection.Learning(2.5, 4.5, StartUtc, 0, StartUtc.AddMinutes(10));
        var learningCard = learningProj.ToCard();
        Assert.AreEqual(Fsrs6CardState.Learning, learningCard.State);
        Assert.AreEqual(2.5, learningCard.Stability);
        Assert.AreEqual(4.5, learningCard.Difficulty);
        Assert.AreEqual(StartUtc, learningCard.LastReviewedAtUtc);
        Assert.AreEqual(0, learningCard.StepIndex);
        Assert.AreEqual(StartUtc.AddMinutes(10), learningCard.DueAtUtc);

        var learningRoundTrip = Fsrs6ScheduleProjection.FromCard(learningCard);
        Assert.AreEqual(learningProj, learningRoundTrip);

        // Review
        var reviewProj = Fsrs6ScheduleProjection.Review(12.0, 5.0, StartUtc, StartUtc.AddDays(12));
        var reviewCard = reviewProj.ToCard();
        Assert.AreEqual(Fsrs6CardState.Review, reviewCard.State);
        Assert.AreEqual(12.0, reviewCard.Stability);
        Assert.AreEqual(5.0, reviewCard.Difficulty);
        Assert.AreEqual(StartUtc, reviewCard.LastReviewedAtUtc);
        Assert.IsNull(reviewCard.StepIndex);
        Assert.AreEqual(StartUtc.AddDays(12), reviewCard.DueAtUtc);

        var reviewRoundTrip = Fsrs6ScheduleProjection.FromCard(reviewCard);
        Assert.AreEqual(reviewProj, reviewRoundTrip);

        // Relearning
        var relearningProj = Fsrs6ScheduleProjection.Relearning(1.8, 6.2, StartUtc, 0, StartUtc.AddMinutes(10));
        var relearningCard = relearningProj.ToCard();
        Assert.AreEqual(Fsrs6CardState.Relearning, relearningCard.State);
        Assert.AreEqual(1.8, relearningCard.Stability);
        Assert.AreEqual(6.2, relearningCard.Difficulty);
        Assert.AreEqual(StartUtc, relearningCard.LastReviewedAtUtc);
        Assert.AreEqual(0, relearningCard.StepIndex);
        Assert.AreEqual(StartUtc.AddMinutes(10), relearningCard.DueAtUtc);

        var relearningRoundTrip = Fsrs6ScheduleProjection.FromCard(relearningCard);
        Assert.AreEqual(relearningProj, relearningRoundTrip);
    }

    [TestMethod]
    public void Projection_RejectsMalformedOrCorruptState_FailsClosedWithCorruptionException()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.FromHours(2));

        // New card invariants
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.New, stability: 1.0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.New, difficulty: 5.0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.New, lastReviewedAtUtc: StartUtc));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.New, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.New, dueAtUtc: nonUtc));

        // Learning card invariants
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: null, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: double.NaN, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: double.PositiveInfinity, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 0.0005, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: null, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 0.5, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 10.5, lastReviewedAtUtc: StartUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: null, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: nonUtc, stepIndex: 0));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 1));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Learning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: null));

        // Review card invariants
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Review, stability: 10.0, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 0));

        // Relearning card invariants
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Relearning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: 1));
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection(Fsrs6CardState.Relearning, stability: 2.0, difficulty: 5.0, lastReviewedAtUtc: StartUtc, stepIndex: null));

        // Undefined state enum
        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ScheduleProjection((Fsrs6CardState)999));
    }

    [TestMethod]
    public void ReviewFact_ValidatesUtcAndEnum_MapsCorrectly()
    {
        var nonUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.FromHours(1));

        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ReviewFact(nonUtc, ReviewRating.Good));

        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            new Fsrs6ReviewFact(StartUtc, (ReviewRating)999));

        var validFact = new Fsrs6ReviewFact(StartUtc, ReviewRating.Good);
        Assert.AreEqual(StartUtc, validFact.ReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, validFact.Rating);

        var reviewEvent = validFact.ToReviewEvent();
        Assert.AreEqual(StartUtc, reviewEvent.ReviewedAtUtc);
        Assert.AreEqual(ReviewRating.Good, reviewEvent.Rating);

        var roundTrip = Fsrs6ReviewFact.FromReviewEvent(reviewEvent);
        Assert.AreEqual(validFact, roundTrip);
    }

    [TestMethod]
    public void Scheduling_UsesInjectedClockTimestamp()
    {
        var reviewTime = new DateTime(2026, 8, 28, 14, 30, 0, DateTimeKind.Utc);
        var clock = new LocalFakeClock(reviewTime);
        var service = new Fsrs6SchedulingService(clock);

        var initial = Fsrs6ScheduleProjection.New();
        var scheduled = service.Schedule(initial, ReviewRating.Good);

        Assert.AreEqual(new DateTimeOffset(reviewTime, TimeSpan.Zero), scheduled.LastReviewedAtUtc);
    }

    [TestMethod]
    public void Scheduling_NonUtcClockValue_FailsClosed()
    {
        var localTime = new DateTime(2026, 8, 28, 14, 30, 0, DateTimeKind.Local);
        var unspecifiedTime = new DateTime(2026, 8, 28, 14, 30, 0, DateTimeKind.Unspecified);

        var serviceLocal = new Fsrs6SchedulingService(new BadKindClock(localTime));
        var serviceUnspecified = new Fsrs6SchedulingService(new BadKindClock(unspecifiedTime));

        var initial = Fsrs6ScheduleProjection.New();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            serviceLocal.Schedule(initial, ReviewRating.Good));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            serviceUnspecified.Schedule(initial, ReviewRating.Good));
    }

    [TestMethod]
    public void Scheduling_Transitions_DelegateCorrectly()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        var initial = Fsrs6ScheduleProjection.New();

        // New -> Again: Learning, 10 min step, stepIndex 0
        var again = service.Schedule(initial, ReviewRating.Again);
        Assert.AreEqual(Fsrs6CardState.Learning, again.State);
        Assert.AreEqual(0, again.StepIndex);
        Assert.AreEqual(StartUtc.AddMinutes(10), again.DueAtUtc);

        // New -> Hard: Learning, 15 min step, stepIndex 0
        var hard = service.Schedule(initial, ReviewRating.Hard);
        Assert.AreEqual(Fsrs6CardState.Learning, hard.State);
        Assert.AreEqual(0, hard.StepIndex);
        Assert.AreEqual(StartUtc.AddMinutes(15), hard.DueAtUtc);

        // New -> Good: Review, graduated, stepIndex null
        var good = service.Schedule(initial, ReviewRating.Good);
        Assert.AreEqual(Fsrs6CardState.Review, good.State);
        Assert.IsNull(good.StepIndex);
        Assert.IsTrue(good.DueAtUtc > StartUtc.AddHours(20));

        // New -> Easy: Review, graduated, stepIndex null, interval > Good
        var easy = service.Schedule(initial, ReviewRating.Easy);
        Assert.AreEqual(Fsrs6CardState.Review, easy.State);
        Assert.IsNull(easy.StepIndex);
        Assert.IsTrue(easy.DueAtUtc > good.DueAtUtc);

        // Review -> Again: Relearning, 10 min step, stepIndex 0
        var reviewInitial = Fsrs6ScheduleProjection.Review(10.0, 5.0, StartUtc.AddDays(-10));
        var reviewAgain = service.Schedule(reviewInitial, ReviewRating.Again);
        Assert.AreEqual(Fsrs6CardState.Relearning, reviewAgain.State);
        Assert.AreEqual(0, reviewAgain.StepIndex);
        Assert.AreEqual(StartUtc.AddMinutes(10), reviewAgain.DueAtUtc);
    }

    [TestMethod]
    public void Scheduling_NullArgumentsOrCorruptProjection_FailsClosed()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new Fsrs6SchedulingService(null!));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            service.Schedule(null!, ReviewRating.Good));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            service.Schedule(Fsrs6ScheduleProjection.New(), (ReviewRating)999));
    }

    [TestMethod]
    public void Replay_DeterministicallyRebuildsProjection_MatchesCoreReplayer()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);
        var coreScheduler = new Fsrs6Scheduler();
        var coreReplayer = new Fsrs6Replayer(coreScheduler);

        var initial = Fsrs6ScheduleProjection.New();
        var coreInitial = initial.ToCard();

        Fsrs6ReviewFact[] facts =
        [
            new(StartUtc, ReviewRating.Again),
            new(StartUtc.AddMinutes(10), ReviewRating.Good),
            new(StartUtc.AddDays(4), ReviewRating.Good),
            new(StartUtc.AddDays(15), ReviewRating.Hard),
            new(StartUtc.AddDays(25), ReviewRating.Easy)
        ];

        var coreEvents = facts.Select(f => f.ToReviewEvent()).ToArray();

        var expectedCard = coreReplayer.Replay(coreInitial, coreEvents);
        var expectedProjection = Fsrs6ScheduleProjection.FromCard(expectedCard);

        var actualProjection1 = service.Replay(initial, facts);
        var actualProjection2 = service.Replay(initial, facts);

        Assert.AreEqual(expectedProjection, actualProjection1);
        Assert.AreEqual(actualProjection1, actualProjection2);
    }

    [TestMethod]
    public void Replay_PreservesEqualTimestampOrder()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        var initial = Fsrs6ScheduleProjection.New();

        // Two events at the identical timestamp
        Fsrs6ReviewFact[] facts =
        [
            new(StartUtc, ReviewRating.Again),
            new(StartUtc, ReviewRating.Good)
        ];

        var result = service.Replay(initial, facts);
        Assert.AreEqual(Fsrs6CardState.Review, result.State);
    }

    [TestMethod]
    public void Replay_ChronologicalReversal_FailsClosedWithCorruptionException()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        var initial = Fsrs6ScheduleProjection.New();

        Fsrs6ReviewFact[] reversedFacts =
        [
            new(StartUtc.AddDays(10), ReviewRating.Good),
            new(StartUtc.AddDays(2), ReviewRating.Good)
        ];

        var ex = Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            service.Replay(initial, reversedFacts));

        Assert.IsInstanceOfType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [TestMethod]
    public void Replay_DoesNotMutateInputHistoryOrInitialProjection()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        var initial = Fsrs6ScheduleProjection.New();
        Fsrs6ReviewFact[] facts =
        [
            new(StartUtc, ReviewRating.Again),
            new(StartUtc.AddMinutes(10), ReviewRating.Good)
        ];

        var snapshot = facts.ToArray();

        _ = service.Replay(initial, facts);

        CollectionAssert.AreEqual(snapshot, facts);
        Assert.AreEqual(Fsrs6CardState.New, initial.State);
    }

    [TestMethod]
    public void Replay_NullArguments_ThrowsArgumentNullException()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            service.Replay(null!, Array.Empty<Fsrs6ReviewFact>()));

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            service.Replay(Fsrs6ScheduleProjection.New(), null!));
    }

    [TestMethod]
    public void Projection_SchedulingStateProperties_AreGetOnlyWithoutSetOrInitAccessors()
    {
        var stateProperties = new[]
        {
            nameof(Fsrs6ScheduleProjection.State),
            nameof(Fsrs6ScheduleProjection.Stability),
            nameof(Fsrs6ScheduleProjection.Difficulty),
            nameof(Fsrs6ScheduleProjection.LastReviewedAtUtc),
            nameof(Fsrs6ScheduleProjection.StepIndex),
            nameof(Fsrs6ScheduleProjection.DueAtUtc)
        };

        foreach (var propertyName in stateProperties)
        {
            var property = typeof(Fsrs6ScheduleProjection).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);

            Assert.IsNotNull(property, $"Property '{propertyName}' must exist on Fsrs6ScheduleProjection.");
            Assert.IsTrue(property.CanRead, $"Property '{propertyName}' must be readable.");
            Assert.IsFalse(property.CanWrite, $"Property '{propertyName}' must not have a set or init accessor.");
            Assert.IsNull(property.SetMethod, $"Property '{propertyName}' must have no SetMethod.");
        }
    }

    [TestMethod]
    public void ReviewFact_DefaultValue_ToReviewEventFailsClosedWithCorruptionException()
    {
        var defaultFact = default(Fsrs6ReviewFact);

        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            defaultFact.ToReviewEvent());
    }

    [TestMethod]
    public void Replay_ContainingDefaultReviewFact_FailsClosedWithCorruptionException()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);
        var initial = Fsrs6ScheduleProjection.New();

        Fsrs6ReviewFact[] factsWithDefault = [default];

        Assert.ThrowsExactly<LearningScheduleCorruptionException>(() =>
            service.Replay(initial, factsWithDefault));
    }

    [TestMethod]
    public void SchedulingService_Constructor_DoesNotAcceptIndependentReplayer()
    {
        var constructors = typeof(Fsrs6SchedulingService).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach (var ctor in constructors)
        {
            var parameters = ctor.GetParameters();
            Assert.IsFalse(
                parameters.Any(p => p.ParameterType == typeof(Fsrs6Replayer)),
                "Fsrs6SchedulingService constructor must not accept Fsrs6Replayer parameter.");
        }
    }

    [TestMethod]
    public void Replay_UnrelatedEnumerableException_PropagatesUnchangedWithoutReclassification()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);
        var initial = Fsrs6ScheduleProjection.New();

        var expectedException = new InvalidOperationException("Unrelated database iterator failure.");

        IEnumerable<Fsrs6ReviewFact> ThrowingSequence()
        {
            yield return new Fsrs6ReviewFact(StartUtc, ReviewRating.Good);
            throw expectedException;
        }

        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
            service.Replay(initial, ThrowingSequence()));

        Assert.AreSame(expectedException, thrown);
    }

    [TestMethod]
    public void Replay_ProgrammingContractExceptionInEnumerable_PropagatesUnchangedWithoutReclassification()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var service = new Fsrs6SchedulingService(clock);
        var initial = Fsrs6ScheduleProjection.New();

        var expectedException = new ArgumentNullException("source", "Unrelated caller argument null.");

        IEnumerable<Fsrs6ReviewFact> ThrowingSequence()
        {
            yield return new Fsrs6ReviewFact(StartUtc, ReviewRating.Good);
            throw expectedException;
        }

        var thrown = Assert.ThrowsExactly<ArgumentNullException>(() =>
            service.Replay(initial, ThrowingSequence()));

        Assert.AreSame(expectedException, thrown);
    }

    [TestMethod]
    public void SchedulingService_WithCustomScheduler_UsesAuthoritativeSchedulerForBothScheduleAndReplay()
    {
        var clock = new LocalFakeClock(StartUtc.UtcDateTime);
        var customParams = new Fsrs6Parameters(Fsrs6Parameters.DefaultWeights, desiredRetention: 0.80);
        var customScheduler = new Fsrs6Scheduler(customParams);
        var defaultScheduler = new Fsrs6Scheduler();

        var customService = new Fsrs6SchedulingService(clock, customScheduler);
        var defaultService = new Fsrs6SchedulingService(clock, defaultScheduler);

        var initial = Fsrs6ScheduleProjection.New();

        // Schedule
        var customScheduled = customService.Schedule(initial, ReviewRating.Good);
        var defaultScheduled = defaultService.Schedule(initial, ReviewRating.Good);

        // Replay
        Fsrs6ReviewFact[] facts = [new(StartUtc, ReviewRating.Good)];
        var customReplayed = customService.Replay(initial, facts);
        var defaultReplayed = defaultService.Replay(initial, facts);

        // Both schedule and replay in customService produce identical results governed by the custom scheduler
        Assert.AreEqual(customScheduled, customReplayed);

        // And they differ from default desired retention results
        Assert.AreNotEqual(defaultScheduled.DueAtUtc, customScheduled.DueAtUtc);
        Assert.AreNotEqual(defaultReplayed.DueAtUtc, customReplayed.DueAtUtc);
    }
}