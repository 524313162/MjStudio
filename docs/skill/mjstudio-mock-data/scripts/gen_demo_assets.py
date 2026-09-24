"""生成模拟资产数据：2张PNG图片 + 2段WAV音频，放入 resources\测试漫剧 目录"""
import os
import struct
import zlib
import math
import wave
import random

BASE = r"d:\00-ai-project\MjStudio\resources\测试漫剧"
IMG_DIR = os.path.join(BASE, "images")
AUD_DIR = os.path.join(BASE, "audio")
os.makedirs(IMG_DIR, exist_ok=True)
os.makedirs(AUD_DIR, exist_ok=True)


def make_png(width, height, pixels, path):
    """pixels: list of (r,g,b) rows"""
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)

    raw = b""
    for row in pixels:
        raw += b"\x00" + b"".join(struct.pack("BBB", *px) for px in row)

    ihdr = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", ihdr)
    png += chunk(b"IDAT", zlib.compress(raw, 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)
    print(f"PNG: {path} ({width}x{height})")


def gradient_image(width, height, c1, c2, path):
    """生成渐变背景 + 简单图形"""
    pixels = []
    for y in range(height):
        row = []
        for x in range(width):
            t = (x + y) / (width + height)
            r = int(c1[0] + (c2[0] - c1[0]) * t)
            g = int(c1[1] + (c2[1] - c1[1]) * t)
            b = int(c1[2] + (c2[2] - c1[2]) * t)
            # 画一个简单的圆
            cx, cy, rad = width // 2, height // 2, min(width, height) // 4
            if (x - cx) ** 2 + (y - cy) ** 2 < rad ** 2:
                r, g, b = 255, 255, 255
            row.append((r, g, b))
        pixels.append(row)
    make_png(width, height, pixels, path)


def make_wav(path, duration=3.0, freq=440.0, sample_rate=44100):
    """生成正弦波 WAV 音频"""
    n = int(duration * sample_rate)
    frames = bytearray()
    for i in range(n):
        t = i / sample_rate
        # 带淡入淡出和颤音
        env = min(1.0, i / (sample_rate * 0.05)) * min(1.0, (n - i) / (sample_rate * 0.1))
        val = 0.6 * env * math.sin(2 * math.pi * freq * t)
        val += 0.2 * env * math.sin(2 * math.pi * freq * 1.5 * t)
        sample = int(max(-1.0, min(1.0, val)) * 32767)
        frames += struct.pack("<h", sample)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        w.writeframes(bytes(frames))
    print(f"WAV: {path} ({duration}s, {freq}Hz)")


# 1. 角色图片（暖色调渐变）
gradient_image(512, 512, (255, 200, 150), (180, 100, 200),
               os.path.join(IMG_DIR, "demo_character.png"))

# 2. 场景图片（冷色调渐变）
gradient_image(768, 432, (100, 180, 255), (30, 60, 120),
               os.path.join(IMG_DIR, "demo_scene.png"))

# 3. BGM 音频（舒缓）
make_wav(os.path.join(AUD_DIR, "demo_bgm.wav"), duration=4.0, freq=330.0)

# 4. 音效音频（短促）
make_wav(os.path.join(AUD_DIR, "demo_sfx.wav"), duration=1.5, freq=880.0)

print("DONE")
