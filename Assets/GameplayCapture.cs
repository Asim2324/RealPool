using System.IO;
using UnityEngine;

// Frame-accurate gameplay capture. Locks game time to a fixed frame rate and writes one PNG per
// frame plus an audio event log; Tools/make_video.py assembles them into an MP4.
// Not part of normal gameplay: only active when added to the scene and enabled.
public class GameplayCapture : MonoBehaviour
{
    public string outputDir = "Capture";
    public int frameRate = 30;
    public float maxSeconds = 120f;
    public bool recording = false;

    int frame = 0;

    public void Begin(string dir)
    {
        outputDir = dir;
        Directory.CreateDirectory(System.IO.Path.Combine(outputDir, "frames"));
        Time.captureFramerate = frameRate;
        frame = 0;
        PoolSfx.BeginLog();
        recording = true;
    }

    public void End()
    {
        if (!recording) return;
        recording = false;
        Time.captureFramerate = 0;
        PoolSfx.EndLog(System.IO.Path.Combine(outputDir, "audio_events.csv"));
        PoolSfx.ExportClips(outputDir);
        File.WriteAllText(System.IO.Path.Combine(outputDir, "frames.txt"), frame.ToString());
    }

    void LateUpdate()
    {
        if (!recording) return;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(outputDir, "frames", $"f{frame:D5}.png"));
        frame++;
        if (frame >= maxSeconds * frameRate) End();
    }
}
