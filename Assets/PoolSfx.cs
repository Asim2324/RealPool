using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// Procedural game audio: cue strike, ball-ball click, cushion thud, pocket drop.
// No audio assets existed in the project, so the clips are synthesized at startup.
// While GameplayCapture is recording, every sound is also logged (game time, name, volume)
// so the exported video's soundtrack can be rebuilt sample-accurately from the same waveforms.
public static class PoolSfx
{
    const int Rate = 44100;
    static AudioSource source;
    static readonly Dictionary<string, AudioClip> clips = new();
    static readonly Dictionary<string, float> lastPlayed = new();

    public static bool logging = false;
    public static float logStartTime = 0f;
    static readonly StringBuilder log = new();

    static void Ensure()
    {
        if (source != null) return;
        var go = new GameObject("PoolSfx");
        Object.DontDestroyOnLoad(go);
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        clips["strike"] = Make("strike", Strike());
        clips["click"] = Make("click", Click());
        clips["cushion"] = Make("cushion", Cushion());
        clips["pocket"] = Make("pocket", Pocket());
    }

    static AudioClip Make(string name, float[] data)
    {
        var c = AudioClip.Create(name, data.Length, 1, Rate, false);
        c.SetData(data, 0);
        return c;
    }

    public static float[] Samples(string name)
    {
        return name switch
        {
            "strike" => Strike(),
            "click" => Click(),
            "cushion" => Cushion(),
            _ => Pocket(),
        };
    }

    // Deterministic noise so the logged soundtrack matches what played in-game.
    static float Noise(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s / (float)uint.MaxValue) * 2f - 1f; }

    static float[] Click()
    {
        int n = (int)(Rate * 0.09f); var d = new float[n]; uint s = 12345;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float env = Mathf.Exp(-t * 70f);
            d[i] = env * (0.55f * Mathf.Sin(2 * Mathf.PI * 3100 * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * 4700 * t) + 0.25f * Noise(ref s) * Mathf.Exp(-t * 300f));
        }
        return d;
    }

    static float[] Strike()
    {
        int n = (int)(Rate * 0.12f); var d = new float[n]; uint s = 777;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            d[i] = Mathf.Exp(-t * 45f) * (0.6f * Mathf.Sin(2 * Mathf.PI * 1200 * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * 380 * t)) + 0.35f * Noise(ref s) * Mathf.Exp(-t * 220f);
        }
        return d;
    }

    static float[] Cushion()
    {
        int n = (int)(Rate * 0.14f); var d = new float[n]; uint s = 4242;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            d[i] = Mathf.Exp(-t * 35f) * (0.7f * Mathf.Sin(2 * Mathf.PI * 140 * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * 260 * t)) + 0.12f * Noise(ref s) * Mathf.Exp(-t * 120f);
        }
        return d;
    }

    static float[] Pocket()
    {
        int n = (int)(Rate * 0.45f); var d = new float[n]; uint s = 99;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float thunk = Mathf.Exp(-t * 18f) * Mathf.Sin(2 * Mathf.PI * (110 - 40 * t) * t);
            float rattle = (t > 0.08f ? Mathf.Exp(-(t - 0.08f) * 14f) : 0) * 0.25f * Noise(ref s) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 28 * t));
            d[i] = 0.8f * thunk + rattle;
        }
        return d;
    }

    public static void Play(string name, float volume)
    {
        volume = Mathf.Clamp01(volume);
        if (volume < 0.03f) return;
        Ensure();
        // Rate-limit identical sounds so simultaneous contacts don't stack into clipping.
        if (lastPlayed.TryGetValue(name, out float last) && Time.time - last < 0.03f) return;
        lastPlayed[name] = Time.time;
        source.PlayOneShot(clips[name], volume);
        if (logging)
            log.Append((Time.time - logStartTime).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(name).Append(',')
               .Append(volume.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
    }

    public static void BeginLog() { log.Clear(); logStartTime = Time.time; logging = true; }

    public static void EndLog(string path) { logging = false; File.WriteAllText(path, log.ToString()); }

    public static void ExportClips(string dir)
    {
        foreach (string n in new[] { "strike", "click", "cushion", "pocket" })
        {
            var data = Samples(n);
            var sb = new StringBuilder();
            foreach (float f in data) sb.Append(f.ToString("F5", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(System.IO.Path.Combine(dir, n + ".txt"), sb.ToString());
        }
    }
}
