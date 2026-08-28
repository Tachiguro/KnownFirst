using System.Collections.Immutable;
using KnownFirst.Core.Learning.Fsrs6;

namespace KnownFirst.Tests;

[TestClass]
public sealed class Fsrs6ParameterValidationTests
{
    private static readonly double[] ApprovedWeights =
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

    [TestMethod]
    public void ProductionParameters_ContainsExactlyApproved21ValuesInExactOrder()
    {
        var parameters = Fsrs6Parameters.Default;

        Assert.HasCount(21, parameters.Weights);
        CollectionAssert.AreEqual(ApprovedWeights, parameters.Weights.ToArray());
    }

    [TestMethod]
    public void ProductionParameters_DefaultsCannotBeMutated()
    {
        var weights = Fsrs6Parameters.Default.Weights;

        Assert.IsInstanceOfType<ImmutableArray<double>>(weights);
        Assert.AreSame(Fsrs6Parameters.Default, Fsrs6Parameters.Default);
    }

    [TestMethod]
    public void ProductionParameters_DesiredRetentionIsExactlyApprovedDefault()
    {
        Assert.AreEqual(0.90, Fsrs6Parameters.Default.DesiredRetention);
    }

    [TestMethod]
    public void ProductionParameters_MaximumIntervalIsExactly36500()
    {
        Assert.AreEqual(36500, Fsrs6Parameters.Default.MaximumIntervalDays);
    }

    [TestMethod]
    public void ProductionParameters_FuzzIsDisabled()
    {
        Assert.IsFalse(Fsrs6Parameters.Default.EnableFuzz);
    }

    [TestMethod]
    public void ProductionParameters_AlgorithmNameIsFsrs6()
    {
        Assert.AreEqual("FSRS-6", Fsrs6Parameters.Default.Algorithm);
    }

    [TestMethod]
    public void Validation_RejectsParameterCountNotEqualTo21()
    {
        // 20 weights
        var tooFew = ApprovedWeights.Take(20).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(tooFew));

        // 22 weights
        var tooMany = ApprovedWeights.Concat([1.0]).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(tooMany));

        // Empty
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(Array.Empty<double>()));
    }

    [TestMethod]
    public void Validation_RejectsNullWeights()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new Fsrs6Parameters(null!));
    }

    [TestMethod]
    public void Validation_RejectsEveryNonFiniteWeight()
    {
        for (int i = 0; i < ApprovedWeights.Length; i++)
        {
            var nanWeights = (double[])ApprovedWeights.Clone();
            nanWeights[i] = double.NaN;
            Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(nanWeights), $"Weight at index {i} as NaN must be rejected.");

            var posInfWeights = (double[])ApprovedWeights.Clone();
            posInfWeights[i] = double.PositiveInfinity;
            Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(posInfWeights), $"Weight at index {i} as +Inf must be rejected.");

            var negInfWeights = (double[])ApprovedWeights.Clone();
            negInfWeights[i] = double.NegativeInfinity;
            Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(negInfWeights), $"Weight at index {i} as -Inf must be rejected.");
        }
    }

    [TestMethod]
    public void Validation_RejectsInvalidDesiredRetention()
    {
        // Boundary cases: <= 0.0 or >= 1.0
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: -0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: 1.05));

        // Non-finite
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: double.PositiveInfinity));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, desiredRetention: double.NegativeInfinity));
    }

    [TestMethod]
    public void Validation_AcceptsValidDesiredRetention()
    {
        var p1 = new Fsrs6Parameters(ApprovedWeights, desiredRetention: 0.70);
        var p2 = new Fsrs6Parameters(ApprovedWeights, desiredRetention: 0.85);
        var p3 = new Fsrs6Parameters(ApprovedWeights, desiredRetention: 0.90);
        var p4 = new Fsrs6Parameters(ApprovedWeights, desiredRetention: 0.99);

        Assert.AreEqual(0.70, p1.DesiredRetention);
        Assert.AreEqual(0.85, p2.DesiredRetention);
        Assert.AreEqual(0.90, p3.DesiredRetention);
        Assert.AreEqual(0.99, p4.DesiredRetention);
    }

    [TestMethod]
    public void Validation_RejectsNonPositiveMaximumInterval()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: -36500));
    }

    [TestMethod]
    public void Validation_AcceptsValidMaximumInterval()
    {
        var p1 = new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: 1);
        var p2 = new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: 36500);
        var p3 = new Fsrs6Parameters(ApprovedWeights, maximumIntervalDays: 100);

        Assert.AreEqual(1, p1.MaximumIntervalDays);
        Assert.AreEqual(36500, p2.MaximumIntervalDays);
        Assert.AreEqual(100, p3.MaximumIntervalDays);
    }

    [TestMethod]
    public void Validation_RejectsFuzzEnabled()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Fsrs6Parameters(ApprovedWeights, enableFuzz: true));
    }
}
