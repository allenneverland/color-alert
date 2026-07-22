using ColorAlert.Core;

namespace ColorAlert.Core.Tests;

[TestClass]
public sealed class AlertStateMachineTests
{
    private static readonly DetectionSettings Settings = new()
    {
        Sensitivity = 50,
        StableFrameCount = 3,
    };

    [TestMethod]
    public void SmallMismatchDoesNotTrigger()
    {
        var detector = new AlertStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.IsFalse(detector.IsAlerted);
    }

    [TestMethod]
    public void StableMismatchFramesTriggerOnlyOnce()
    {
        var detector = new AlertStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.01, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.01, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.50, Settings));
        Assert.IsTrue(detector.IsAlerted);
    }

    [TestMethod]
    public void StableMatchingFramesRearmForNextAlert()
    {
        var detector = new AlertStateMachine();
        _ = detector.Observe(0.02, Settings);
        _ = detector.Observe(0.02, Settings);
        _ = detector.Observe(0.02, Settings);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.005, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.004, Settings));
        Assert.AreEqual(AlertTransition.Rearmed, detector.Observe(0.005, Settings));
        Assert.IsFalse(detector.IsAlerted);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.02, Settings));
    }

    [TestMethod]
    [DataRow(1, 64, 0.20)]
    [DataRow(50, 12, 0.01)]
    [DataRow(100, 2, 0.001)]
    public void SensitivityMapsToExpectedThresholds(
        int sensitivity,
        int expectedTolerance,
        double expectedRatio)
    {
        var thresholds = DetectionSettings.GetThresholds(sensitivity);

        Assert.AreEqual(expectedTolerance, thresholds.PixelDifferenceTolerance);
        Assert.AreEqual(expectedRatio, thresholds.ChangedPixelRatio, 0.000_001);
    }

    [TestMethod]
    public void MultipleRegionsCoalesceEachCycleAndKeepIndependentState()
    {
        var firstRegionId = Guid.NewGuid();
        var secondRegionId = Guid.NewGuid();
        var laterRegionId = Guid.NewGuid();
        var coordinator = new MultiRegionAlertCoordinator();
        coordinator.Synchronize([firstRegionId, secondRegionId, laterRegionId]);
        var immediateSettings = Settings with { StableFrameCount = 1 };

        var simultaneous = coordinator.Observe(
            [
                new RegionObservation(firstRegionId, 0.02),
                new RegionObservation(secondRegionId, 0.02),
            ],
            immediateSettings);

        Assert.IsTrue(simultaneous.ShouldAlert);
        Assert.AreEqual(2, simultaneous.Regions.Count);
        Assert.IsTrue(simultaneous.Regions.All(region =>
            region.Transition == AlertTransition.Triggered));

        var later = coordinator.Observe(
            [new RegionObservation(laterRegionId, 0.02)],
            immediateSettings);
        Assert.IsTrue(later.ShouldAlert);

        var rearmed = coordinator.Observe(
            [new RegionObservation(firstRegionId, 0d)],
            immediateSettings);
        Assert.AreEqual(AlertTransition.Rearmed, rearmed.Regions[0].Transition);

        var triggeredAgain = coordinator.Observe(
            [new RegionObservation(firstRegionId, 0.02)],
            immediateSettings);
        Assert.IsTrue(triggeredAgain.ShouldAlert);
    }
}
