"""Regenerate the five-frame VFR regression fixture using FFmpeg with libx264."""
from pathlib import Path
import subprocess
import tempfile

target = Path(__file__).with_name("variable-rate.mp4")
with tempfile.TemporaryDirectory() as temp:
    folder = Path(temp)
    for frame in range(5):
        pixels = bytearray([frame * 50] * (160 * 120))
        for bit in range(3):
            value = 255 if (frame + 1) & (1 << bit) else 0
            for y in range(8, 24):
                start = y * 160 + 8 + 24 * bit
                pixels[start:start + 16] = bytes([value]) * 16
        (folder / f"{frame}.pgm").write_bytes(b"P5\n160 120\n255\n" + pixels)
    subprocess.run([
        "ffmpeg", "-loglevel", "error", "-y", "-framerate", "120",
        "-i", str(folder / "%d.pgm"),
        "-vf", "settb=1/120,setpts='if(eq(N,0),0,if(eq(N,1),2,if(eq(N,2),7,if(eq(N,3),9,15))))'",
        "-fps_mode", "vfr", "-enc_time_base", "1:120", "-c:v", "libx264", "-bf", "0",
        "-crf", "12", "-pix_fmt", "yuv420p", "-video_track_timescale", "120000",
        "-movflags", "+faststart", str(target),
    ], check=True)
