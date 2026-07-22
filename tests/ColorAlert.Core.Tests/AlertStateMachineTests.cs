using ColorAlert.Core;

namespace ColorAlert.Core.Tests;

[TestClass]
public sealed class AlertStateMachineTests
{
    private static readonly DetectionSettings Settings = new()
    {
        TriggerRatio = 0.01,
        StableFrameCount = 3,
    };

    [TestMethod]
    public void BelowThresholdDoesNotTrigger()
    {
        var detector = new AlertStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.009, Settings));
        Assert.IsFalse(detector.IsAlerted);
    }

    [TestMethod]
    public void StableNonBlackFramesTriggerOnlyOnce()
    {
        var detector = new AlertStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.01, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.02, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.01, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.50, Settings));
        Assert.IsTrue(detector.IsAlerted);
    }

    [TestMethod]
    public void StableBlackFramesRearmForNextAlert()
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
}
