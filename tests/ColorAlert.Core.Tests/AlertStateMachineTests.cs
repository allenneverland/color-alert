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

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    public void StableAreaIncreaseTriggersWithoutRepeatingSameArea()
    {
        var detector = new TargetAreaStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.031, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.031, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.031, Settings));
    }

    [TestMethod]
    public void StableDecreaseRebasesAndCompleteDisappearanceClears()
    {
        var detector = new TargetAreaStateMachine();
        _ = detector.Observe(0.02, Settings);
        _ = detector.Observe(0.02, Settings);
        _ = detector.Observe(0.02, Settings);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.014, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.014, Settings));
        Assert.AreEqual(AlertTransition.Rebased, detector.Observe(0.014, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.025, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.025, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.025, Settings));

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.004, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.004, Settings));
        Assert.AreEqual(AlertTransition.Cleared, detector.Observe(0.004, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    [DataRow(1, 2, 0.20)]
    [DataRow(50, 12, 0.01)]
    [DataRow(100, 24, 0.001)]
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
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.02),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.02),
            ],
            immediateSettings);

        Assert.IsTrue(simultaneous.ShouldAlert);
        Assert.AreEqual(2, simultaneous.Targets.Count);

        var unchanged = coordinator.Observe(
            [
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.02),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.02),
            ],
            immediateSettings);
        Assert.IsFalse(unchanged.ShouldAlert);

        var newBlue = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Blue, 0.02)],
            immediateSettings);
        Assert.IsTrue(newBlue.ShouldAlert);

        var moreYellow = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.031)],
            immediateSettings);
        Assert.IsTrue(moreYellow.ShouldAlert);
    }
}
