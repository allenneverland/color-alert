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

        Assert.AreEqual(expectedTolerance, thresholds.ColorTolerance);
        Assert.AreEqual(expectedRatio, thresholds.TriggerRatio, 0.000_001);
    }
}
