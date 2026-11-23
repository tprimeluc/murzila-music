"""Генератор собственного демо-трека (синус), чтобы проверить плеер без чужих файлов.

Пример:
  python scripts/make-demo-track.py 7 demo-demo.wav   # 7 минут, проверить правило 5 ₽
"""
import math
import struct
import sys
import wave

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 20
out = sys.argv[2] if len(sys.argv) > 2 else "demo-track.wav"
rate = 8000

with wave.open(out, "wb") as wav:
    wav.setnchannels(1)
    wav.setsampwidth(2)
    wav.setframerate(rate)
    frames = bytearray()
    for i in range(int(seconds * rate)):
        # простая мелодия, полностью сгенерированная локально
        t = i / rate
        freq = 220 + 110 * (int(t) % 4)
        value = int(8000 * math.sin(2 * math.pi * freq * t) * (0.5 + 0.5 * math.sin(2 * math.pi * 0.5 * t)))
        frames += struct.pack("<h", value)
    wav.writeframes(frames)

print(f"Готово: {out} ({seconds:.0f} c)")
