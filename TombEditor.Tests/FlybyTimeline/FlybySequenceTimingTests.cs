using System.Numerics;
using TombEditor.Controls.FlybyTimeline;
using TombLib;
using TombLib.LevelData;

namespace TombEditor.Tests.FlybyTimeline;

[TestClass]
public class FlybySequenceTimingTests
{
    [TestMethod]
    public void Build_ReturnsEmptyTimingForEmptySequence()
    {
        var timing = FlybySequenceTiming.Build([], useSmoothPause: false);

        Assert.AreEqual(0, timing.CameraCount);
        Assert.AreEqual(0.0f, timing.TotalDuration, 0.001f);
        Assert.AreEqual(0, timing.SplineTimeline.Count);
        Assert.AreEqual(0, timing.CutRegions.Count);
    }

    [TestMethod]
    public void Build_AddsFreezeDurationToLaterCameraTimes()
    {
        var level = FlybyTestFactory.CreateLevel();
        var cameras = FlybyTestFactory.CreateLinearSequence(level.Rooms[0], 1,
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1024.0f),
            new Vector3(0.0f, 0.0f, 2048.0f));

        cameras[1].Flags = FlybyConstants.FlagFreezeCamera;
        cameras[1].Timer = FlybyTestFactory.FreezeFrames(30);

        var timing = FlybySequenceTiming.Build(cameras, useSmoothPause: false);

        Assert.AreEqual(1.0f, timing.GetFreezeDuration(1), 0.001f);
        Assert.IsTrue(timing.GetCameraTime(2) - timing.GetCameraTime(1) >= 1.0f);
    }

    [TestMethod]
    public void Build_WithSmoothPauseDelaysTheFreezeBoundary()
    {
        var level = FlybyTestFactory.CreateLevel(TRVersion.Game.TombEngine);
        var cameras = FlybyTestFactory.CreateLinearSequence(level.Rooms[0], 2,
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1024.0f),
            new Vector3(0.0f, 0.0f, 2048.0f));

        cameras[1].Flags = FlybyConstants.FlagFreezeCamera;
        cameras[1].Timer = FlybyTestFactory.FreezeFrames(30);

        var standardTiming = FlybySequenceTiming.Build(cameras, useSmoothPause: false);
        var smoothTiming = FlybySequenceTiming.Build(cameras, useSmoothPause: true);

        Assert.IsTrue(smoothTiming.GetCameraTime(1) > standardTiming.GetCameraTime(1));
        Assert.IsTrue(smoothTiming.TotalDuration > standardTiming.TotalDuration);
    }

    [TestMethod]
    public void Build_CapturesCutRegionsAndBypassDurations()
    {
        var level = FlybyTestFactory.CreateLevel();
        var cameras = FlybyTestFactory.CreateLinearSequence(level.Rooms[0], 3,
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1024.0f),
            new Vector3(0.0f, 0.0f, 2048.0f),
            new Vector3(0.0f, 0.0f, 3072.0f));

        cameras[1].Flags = FlybyConstants.FlagCameraCut;
        cameras[1].Timer = 3;

        var timing = FlybySequenceTiming.Build(cameras, useSmoothPause: false);

        Assert.AreEqual(1, timing.CutRegions.Count);
        Assert.IsTrue(timing.GetCutBypassDuration(1) > 0.0f);
        Assert.IsTrue(timing.CutRegions[0].EndTime > timing.CutRegions[0].StartTime);
        Assert.IsTrue(timing.TotalDuration >= timing.GetCameraTime(3));
    }
}
