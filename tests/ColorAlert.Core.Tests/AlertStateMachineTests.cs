using ColorAlert.Core;

namespace ColorAlert.Core.Tests;

[TestClass]
public sealed class TargetAreaStateMachineTests
{
    private static readonly DetectionSettings Settings = new()
    {
        Sensitivity = 50,
        StableFrameCount = 3,
    };

    [TestMethod]
    public void AreaBelowMinimumDoesNotTrigger()
    {
        var detector = new TargetAreaStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0009, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    public void StableAreaIncreaseTriggersWithoutRepeatingSameArea()
    {
        var detector = new TargetAreaStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.002, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.002, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.002, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.002, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0031, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0031, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.0031, Settings));
    }

    [TestMethod]
    public void StableDecreaseRebasesAndCompleteDisappearanceClears()
    {
        var detector = new TargetAreaStateMachine();
        _ = detector.Observe(0.002, Settings);
        _ = detector.Observe(0.002, Settings);
        _ = detector.Observe(0.002, Settings);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0014, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0014, Settings));
        Assert.AreEqual(AlertTransition.Rebased, detector.Observe(0.0014, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0025, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0025, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.0025, Settings));

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0004, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0004, Settings));
        Assert.AreEqual(AlertTransition.Cleared, detector.Observe(0.0004, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    [DataRow(1, 2, 0.02)]
    [DataRow(50, 12, 0.001)]
    [DataRow(100, 24, 0.0001)]
    public void SensitivityMapsToExpectedThresholds(
        int sensitivity,
        int expectedTolerance,
        double expectedRatio)
    {
        var thresholds = DetectionSettings.GetThresholds(sensitivity);

        Assert.AreEqual(expectedTolerance, thresholds.ColorTolerance);
        Assert.AreEqual(expectedRatio, thresholds.TargetPixelRatio, 0.000_001);
    }

    [TestMethod]
    public void MultipleTargetsCoalesceEachCycleAndRemainIndependent()
    {
        var firstRegionId = Guid.NewGuid();
        var secondRegionId = Guid.NewGuid();
        var coordinator = new MultiRegionAlertCoordinator();
        coordinator.Synchronize([firstRegionId, secondRegionId]);
        var immediateSettings = Settings with { StableFrameCount = 1 };

        var simultaneous = coordinator.Observe(
            [
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.002),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.002),
            ],
            immediateSettings);

        Assert.IsTrue(simultaneous.ShouldAlert);
        Assert.AreEqual(2, simultaneous.Targets.Count);

        var unchanged = coordinator.Observe(
            [
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.002),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.002),
            ],
            immediateSettings);
        Assert.IsFalse(unchanged.ShouldAlert);

        var newBlue = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Blue, 0.002)],
            immediateSettings);
        Assert.IsTrue(newBlue.ShouldAlert);

        var moreYellow = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.0031)],
            immediateSettings);
        Assert.IsTrue(moreYellow.ShouldAlert);
    }
}
