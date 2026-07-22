using System.Text.Json;
using ColorAlert.Core;

namespace ColorAlert.Core.Tests;

[TestClass]
public sealed class TargetAreaStateMachineTests
{
    private static readonly DetectionSettings Settings = new()
    {
        ColorSensitivity = 50,
        AreaSensitivity = 50,
        StableFrameCount = 3,
    };

    [TestMethod]
    public void AreaBelowMinimumDoesNotTrigger()
    {
        var detector = new TargetAreaStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00009, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00009, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    public void StableAreaIncreaseTriggersWithoutRepeatingSameArea()
    {
        var detector = new TargetAreaStateMachine();

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0002, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0002, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.0002, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.0002, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00031, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00031, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.00031, Settings));
    }

    [TestMethod]
    public void StableDecreaseRebasesAndCompleteDisappearanceClears()
    {
        var detector = new TargetAreaStateMachine();
        _ = detector.Observe(0.0002, Settings);
        _ = detector.Observe(0.0002, Settings);
        _ = detector.Observe(0.0002, Settings);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00014, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00014, Settings));
        Assert.AreEqual(AlertTransition.Rebased, detector.Observe(0.00014, Settings));
        Assert.IsTrue(detector.IsPresent);

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00025, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00025, Settings));
        Assert.AreEqual(AlertTransition.Triggered, detector.Observe(0.00025, Settings));

        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00004, Settings));
        Assert.AreEqual(AlertTransition.None, detector.Observe(0.00004, Settings));
        Assert.AreEqual(AlertTransition.Cleared, detector.Observe(0.00004, Settings));
        Assert.IsFalse(detector.IsPresent);
    }

    [TestMethod]
    [DataRow(1, 100, 12, 0.00001)]
    [DataRow(50, 50, 24, 0.0001)]
    [DataRow(100, 1, 48, 0.001)]
    public void ColorAndAreaSensitivitiesMapIndependently(
        int colorSensitivity,
        int areaSensitivity,
        int expectedTolerance,
        double expectedRatio)
    {
        var settings = new DetectionSettings
        {
            ColorSensitivity = colorSensitivity,
            AreaSensitivity = areaSensitivity,
        };

        Assert.AreEqual(expectedTolerance, settings.ColorTolerance);
        Assert.AreEqual(expectedRatio, settings.TargetPixelRatio, 0.000_000_1);
    }

    [TestMethod]
    public void DefaultsAndPreviousSensitivityScaleMigrateOnce()
    {
        var defaults = new AppSettings().Normalize();
        var previousSettings = JsonSerializer.Deserialize<AppSettings>(
            """{"Detection":{"ColorSensitivity":100,"AreaSensitivity":100}}""")!
            .Normalize();
        var legacySettings = JsonSerializer.Deserialize<AppSettings>(
            """{"Detection":{"Sensitivity":100}}""")!
            .Normalize();
        var normalizedAgain = previousSettings.Normalize();

        Assert.AreEqual(50, defaults.Detection.ColorSensitivity);
        Assert.AreEqual(50, defaults.Detection.AreaSensitivity);
        Assert.AreEqual(50, previousSettings.Detection.ColorSensitivity);
        Assert.AreEqual(50, previousSettings.Detection.AreaSensitivity);
        Assert.AreEqual(50, legacySettings.Detection.ColorSensitivity);
        Assert.AreEqual(50, legacySettings.Detection.AreaSensitivity);
        Assert.AreEqual(50, normalizedAgain.Detection.ColorSensitivity);
        Assert.AreEqual(50, normalizedAgain.Detection.AreaSensitivity);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(previousSettings));
        var detection = document.RootElement.GetProperty(nameof(AppSettings.Detection));
        Assert.AreEqual(
            DetectionSettings.CurrentSensitivityScaleVersion,
            detection.GetProperty(nameof(DetectionSettings.SensitivityScaleVersion)).GetInt32());
        Assert.IsFalse(detection.TryGetProperty(
            nameof(DetectionSettings.Sensitivity),
            out _));
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
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.0002),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.0002),
            ],
            immediateSettings);

        Assert.IsTrue(simultaneous.ShouldAlert);
        Assert.AreEqual(2, simultaneous.Targets.Count);

        var unchanged = coordinator.Observe(
            [
                new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.0002),
                new TargetObservation(secondRegionId, MonitoredColor.Blue, 0.0002),
            ],
            immediateSettings);
        Assert.IsFalse(unchanged.ShouldAlert);

        var newBlue = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Blue, 0.0002)],
            immediateSettings);
        Assert.IsTrue(newBlue.ShouldAlert);

        var moreYellow = coordinator.Observe(
            [new TargetObservation(firstRegionId, MonitoredColor.Yellow, 0.00031)],
            immediateSettings);
        Assert.IsTrue(moreYellow.ShouldAlert);
    }
}
