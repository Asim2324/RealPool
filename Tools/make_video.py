"""Assemble a GameplayCapture folder into an MP4.

usage: python make_video.py <capture_dir> <out.mp4> [segments]
segments: comma list of start-end seconds to keep, e.g. "0-14,20-41" (clean cuts). Default: all.
Audio is rebuilt from audio_events.csv using the exact in-game synthesized waveforms.
"""
import sys, os, wave, struct, subprocess, tempfile

cap, out = sys.argv[1], sys.argv[2]
segs = sys.argv[3] if len(sys.argv) > 3 else None
fps = 30
nframes = int(open(os.path.join(cap, "frames.txt")).read().strip())
dur = nframes / fps
rate = 44100

clips = {n: [float(x) for x in open(os.path.join(cap, n + ".txt")) if x.strip()]
         for n in ("strike", "click", "cushion", "pocket")}
buf = [0.0] * (int(dur * rate) + rate)
for line in open(os.path.join(cap, "audio_events.csv")):
    if not line.strip():
        continue
    t, name, vol = line.strip().split(",")
    start, vol = int(float(t) * rate), float(vol)
    for i, s in enumerate(clips[name]):
        if start + i < len(buf):
            buf[start + i] += s * vol

# soft limiter
peak = max(1e-6, max(abs(x) for x in buf))
gain = min(1.0, 0.9 / peak)
wav = os.path.join(cap, "audio.wav")
with wave.open(wav, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
    w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, x * gain)) * 32767)) for x in buf))

src = ["-framerate", str(fps), "-i", os.path.join(cap, "frames", "f%05d.png"), "-i", wav]
enc = ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
       "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-r", str(fps)]

if not segs:
    cmd = ["ffmpeg", "-y", *src, "-map", "0:v", "-map", "1:a", "-shortest", *enc, out]
else:
    parts = [tuple(map(float, s.split("-"))) for s in segs.split(",")]
    fc, labels = [], []
    for i, (a, b) in enumerate(parts):
        fade = 0.2
        fc.append(f"[0:v]trim={a}:{b},setpts=PTS-STARTPTS,fade=in:st=0:d={fade},fade=out:st={b-a-fade}:d={fade}[v{i}]")
        fc.append(f"[1:a]atrim={a}:{b},asetpts=PTS-STARTPTS,afade=in:st=0:d=0.05,afade=out:st={b-a-0.1}:d=0.1[a{i}]")
        labels.append(f"[v{i}][a{i}]")
    fc.append("".join(labels) + f"concat=n={len(parts)}:v=1:a=1[v][a]")
    cmd = ["ffmpeg", "-y", *src, "-filter_complex", ";".join(fc), "-map", "[v]", "-map", "[a]", *enc, out]
print(" ".join(cmd))
subprocess.run(cmd, check=True)
