"""AF-MilitaryShogi sound effects: wooden pieces on a wooden board.

No external audio material is used. Every sound is synthesized reproducibly (fixed seeds) as
  - an impact transient: a millisecond-long band-limited noise burst, and
  - modal resonances: sums of exponentially damped sinusoids with inharmonic frequencies
    (small hard-wood piece: 1-6 kHz, short decays; thick board: 150-500 Hz, longer decays),
  - a very small room tail (seeded decaying noise, convolved) so it is not bone dry.
Nothing is a single-frequency tone: no beeps, guns, explosions or sirens.

Output (16-bit mono 44.1 kHz WAV): Assets/Generated/Resources/Audio/
  sfx_select.wav            駒を選択「コッ」
  sfx_place_1..3.wav        駒を置く「コトッ」 (3 variants)
  sfx_clash_1..2.wav        駒同士の衝突「カッ」 (2 variants)
  sfx_topple.wav            負けた駒が倒れて盤に当たる「カタン」
  sfx_end.wav               終局の締め（拍子木のような2打）
  audio_manifest.json

Run: python Tools/AudioGen/generate_sfx.py
"""
import hashlib
import json
import os
import wave

import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Generated", "Resources", "Audio")
SR = 44100


def rng_for(name):
    return np.random.default_rng(int(hashlib.sha256(name.encode()).hexdigest()[:8], 16))


def mix(a, b):
    """Sum of two signals of different lengths (the shorter is zero-padded)."""
    n = max(len(a), len(b))
    out = np.zeros(n)
    out[: len(a)] += a
    out[: len(b)] += b
    return out


def silence(seconds):
    return np.zeros(int(SR * seconds))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def transient(rng, ms, lo, hi, amp):
    """Band-limited noise burst: the 'click' of hard wood touching."""
    n = int(SR * ms / 1000.0)
    t = np.arange(n) / SR
    burst = rng.standard_normal(n) * np.exp(-t / (ms / 1000.0 / 3.0))
    return amp * highpass(lowpass(burst, hi), lo)


def modes(rng, freqs, decays_ms, amps, seconds, detune=0.03):
    """Damped sinusoids. Random phase and slight detune per render (seeded)."""
    t = np.arange(int(SR * seconds)) / SR
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays_ms, amps):
        f = f * (1 + rng.uniform(-detune, detune))
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / (d / 1000.0))
    # Soft onset (~0.3 ms) so the modes are excited by the transient, not clicked on.
    ramp = min(len(t), int(SR * 0.0003))
    out[:ramp] *= np.linspace(0, 1, ramp)
    return out


def place_at(buf, sound, seconds, gain=1.0):
    start = int(SR * seconds)
    end = min(len(buf), start + len(sound))
    buf[start:end] += gain * sound[: end - start]


def room(x, rng, ms=90, level=0.06):
    n = int(SR * ms / 1000.0)
    t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-t / (ms / 1000.0 / 4.0))
    ir = lowpass(ir, 3500)
    ir[0] = 0
    wet = np.convolve(x, ir)[: len(x)]
    return x + level * wet / (np.max(np.abs(wet)) + 1e-9) * np.max(np.abs(x))


def piece_hit(rng, strength, brightness=1.0, seconds=0.12, decay_scale=1.0):
    """Small hard-wood piece: inharmonic plate-like modes, short decays."""
    base = rng.uniform(1050, 1250) * brightness
    ratios = np.array([1.0, 1.71, 2.62, 3.83, 5.1])
    decays = np.array([28, 20, 14, 9, 6]) * rng.uniform(0.85, 1.15) * decay_scale
    amps = np.array([1.0, 0.7, 0.45, 0.28, 0.15]) * strength
    return mix(modes(rng, base * ratios, decays, amps, seconds), transient(rng, 1.6, 900, 9000, 0.55 * strength))


def board_thud(rng, strength, seconds=0.2):
    """Thick wooden board: low modes, longer decays, weaker."""
    freqs = np.array([rng.uniform(170, 200), rng.uniform(290, 330), rng.uniform(430, 480)])
    return modes(rng, freqs, np.array([70, 50, 35]), np.array([0.5, 0.35, 0.2]) * strength, seconds)


def finish(x, peak_db=-3.0, fade_ms=8):
    x = x - np.mean(x)
    n = int(SR * fade_ms / 1000.0)
    if n > 0:
        x[-n:] *= np.linspace(1, 0, n)
    x = x / (np.max(np.abs(x)) + 1e-12) * (10 ** (peak_db / 20.0))
    return x


def sfx_select():
    rng = rng_for("select")
    buf = silence(0.07)
    # A light, dry tap: short decays and a relatively strong click.
    place_at(buf, piece_hit(rng, 0.6, brightness=1.35, seconds=0.07, decay_scale=0.35), 0.0)
    place_at(buf, transient(rng, 1.2, 1200, 10000, 0.5), 0.0)
    return finish(room(buf, rng, 50, 0.03), -9.0)


def sfx_place(variant):
    rng = rng_for("place%d" % variant)
    buf = silence(0.18)
    # Edge touches first, then the piece settles flat: two impacts ~10-16 ms apart.
    gap = rng.uniform(0.010, 0.016)
    place_at(buf, piece_hit(rng, 0.45, brightness=0.95, seconds=0.1), 0.0)
    place_at(buf, piece_hit(rng, 1.0, brightness=0.9, seconds=0.12), gap)
    place_at(buf, board_thud(rng, 0.9, seconds=0.16), gap)
    return finish(room(buf, rng), -5.0)


def sfx_clash(variant):
    rng = rng_for("clash%d" % variant)
    buf = silence(0.16)
    # Two pieces hit each other: both ring, slightly different sizes, sharp transient.
    place_at(buf, piece_hit(rng, 1.0, brightness=1.15, seconds=0.14), 0.0)
    place_at(buf, piece_hit(rng, 0.85, brightness=1.0, seconds=0.14), 0.0008)
    place_at(buf, transient(rng, 2.2, 1500, 12000, 0.9), 0.0)
    return finish(room(buf, rng, 70, 0.05), -2.0)


def sfx_topple():
    rng = rng_for("topple")
    buf = silence(0.42)
    # Tilts (light knock), falls flat onto the board (main hit + board), small bounce.
    place_at(buf, piece_hit(rng, 0.35, brightness=1.05, seconds=0.08), 0.0)
    place_at(buf, piece_hit(rng, 1.0, brightness=0.85, seconds=0.14), 0.075)
    place_at(buf, board_thud(rng, 1.2, seconds=0.25), 0.075)
    place_at(buf, piece_hit(rng, 0.3, brightness=0.9, seconds=0.08), 0.16)
    place_at(buf, board_thud(rng, 0.35, seconds=0.12), 0.16)
    return finish(room(buf, rng), -4.0)


def sfx_end():
    rng = rng_for("end")
    buf = silence(1.0)
    # Two claps of hard wooden clappers (拍子木): bright, longer ringing hard-wood modes.
    for at, gain in ((0.0, 1.0), (0.3, 0.9)):
        ratios = np.array([1.0, 2.76, 4.1, 5.4])
        f0 = rng.uniform(930, 980)
        clap = modes(rng, f0 * ratios, np.array([90, 55, 35, 22]), np.array([1.0, 0.55, 0.3, 0.15]) * gain, 0.6, detune=0.005)
        clap = mix(clap, transient(rng, 2.5, 1200, 12000, 1.1 * gain))
        place_at(buf, clap, at)
    return finish(room(buf, rng, 180, 0.12), -3.0)


def write_wav(path, x):
    pcm = np.clip(np.round(x * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    os.makedirs(OUT, exist_ok=True)
    sounds = {"sfx_select": sfx_select(), "sfx_topple": sfx_topple(), "sfx_end": sfx_end()}
    for v in (1, 2, 3):
        sounds["sfx_place_%d" % v] = sfx_place(v)
    for v in (1, 2):
        sounds["sfx_clash_%d" % v] = sfx_clash(v)
    manifest = {"generator": "Tools/AudioGen/generate_sfx.py", "sample_rate": SR, "format": "PCM16 mono",
                "note": "Synthesized; no external audio material.", "outputs": {}}
    for name, data in sorted(sounds.items()):
        path = os.path.join(OUT, name + ".wav")
        write_wav(path, data)
        with open(path, "rb") as f:
            manifest["outputs"]["Assets/Generated/Resources/Audio/" + name + ".wav"] = {
                "sha256": hashlib.sha256(f.read()).hexdigest(), "seconds": round(len(data) / SR, 3)}
    with open(os.path.join(OUT, "audio_manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print("generated %d sounds" % len(sounds))


if __name__ == "__main__":
    main()
