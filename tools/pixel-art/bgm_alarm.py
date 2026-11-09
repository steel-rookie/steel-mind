"""사건 발생용 긴박한 배경음 (직접 합성한 오리지널 루프). 140BPM, D단조, 16마디 ≈ 27초, 끊김 없이 반복."""
import sys, wave
import numpy as np

SR = 32000; BPM = 140; BEAT = 60 / BPM; BARS = 16
N = int(SR * BEAT * 4 * BARS); t_all = np.arange(N) / SR
rng = np.random.default_rng(7)
midi = lambda m: 440 * 2 ** ((m - 69) / 12)

def lp(x, cutoff):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR); X *= 1 / (1 + (f / cutoff) ** 4)
    return np.fft.irfft(X, len(x))

def add(buf, start, sig):
    idx = (np.arange(len(sig)) + start) % N; np.add.at(buf, idx, sig)

out = np.zeros((2, N)); step = BEAT / 4; bar = int(SR * BEAT * 4)

# 1) 8분음 베이스 오스티나토 (D-D-F-D-E-D-C-D), 날카로운 톱니
pattern = [38, 38, 41, 38, 40, 38, 36, 38]
roots = [0, 0, -2, -2, 0, 0, 3, 1]  # 2마디마다 조금씩 이동 (D, C, D, F/E 느낌)
for b in range(BARS):
    shift = roots[(b // 2) % len(roots)]
    for k in range(8):
        m = pattern[k] + shift
        L = int(SR * BEAT / 2 * 0.9); tt = np.arange(L) / SR
        ph = (midi(m) * tt) % 1
        s = (2 * ph - 1) * np.exp(-tt * 6) + 0.5 * np.sin(2 * np.pi * midi(m - 12) * tt) * np.exp(-tt * 4)
        st = int((b * 4 + k * 0.5) * BEAT * SR)
        add(out[0], st, 0.16 * s); add(out[1], st, 0.16 * s)
out = np.stack([lp(out[0], 1400), lp(out[1], 1400)])

# 2) 킥(박마다) + 16분 하이햇 틱
for k in range(BARS * 4):
    L = int(SR * 0.25); tt = np.arange(L) / SR
    f = 50 + 90 * np.exp(-tt * 35); kick = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt * 10)
    add(out[0], int(k * BEAT * SR), 0.28 * kick); add(out[1], int(k * BEAT * SR), 0.28 * kick)
    for q in range(4):
        L2 = int(SR * 0.04); n = rng.standard_normal(L2) * np.exp(-np.arange(L2) / SR * 120)
        n = np.diff(np.r_[0, n])  # 고역만
        g = 0.05 if q % 2 else 0.03
        add(out[0], int((k + q / 4) * BEAT * SR), g * n); add(out[1], int((k + q / 4) * BEAT * SR) + 20, g * n)
    if k % 2 == 1:  # 스네어
        L3 = int(SR * 0.18); tt = np.arange(L3) / SR
        sn = (rng.standard_normal(L3) * 0.7 + np.sin(2 * np.pi * 190 * tt)) * np.exp(-tt * 22)
        add(out[0], int(k * BEAT * SR), 0.12 * sn); add(out[1], int(k * BEAT * SR), 0.12 * sn)

# 3) 경보 사이렌 (두 음 번갈아, 2마디에 한 번씩, 살짝 멀리)
for b in range(0, BARS, 2):
    L = int(SR * BEAT * 4); tt = np.arange(L) / SR
    f = np.where((tt // (BEAT)) % 2 == 0, 880, 660)
    sig = np.sign(np.sin(2 * np.pi * np.cumsum(f) / SR)) * 0.5 + np.sin(2 * np.pi * np.cumsum(f) / SR) * 0.5
    env = np.clip(tt / 0.05, 0, 1) * np.clip((L / SR - tt) / 0.3, 0, 1)
    s = lp(sig * env, 2500)
    add(out[0], b * bar, 0.045 * s); add(out[1], b * bar + 400, 0.035 * s)

# 4) 긴장감 있는 현 화음 (D단조 → B♭ → G단조 → A, 4마디씩), 떨림
chords = [[50, 53, 57], [46, 50, 53], [43, 46, 50], [45, 49, 52]]
for c in range(4):
    L = bar * 4; tt = np.arange(L) / SR
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * 7 * tt)
    s = sum(np.sin(2 * np.pi * (midi(m + 12) + d) * tt) for m in chords[c] for d in (-0.4, 0.4)) / 6
    env = np.clip(tt / 0.4, 0, 1) * np.clip((L / SR - tt) / 0.4, 0, 1)
    add(out[0], c * L, 0.05 * s * trem * env); add(out[1], c * L + 200, 0.05 * s * trem * env)

# 5) 높은 시계 초침 같은 핑 (박마다), 마지막 4마디는 두 배로
for k in range(BARS * 4):
    for half in ((0, 0.5) if k >= (BARS - 4) * 4 else (0,)):
        L = int(SR * 0.06); tt = np.arange(L) / SR
        p = np.sin(2 * np.pi * 2400 * tt) * np.exp(-tt * 80)
        add(out[0], int((k + half) * BEAT * SR), 0.03 * p); add(out[1], int((k + half) * BEAT * SR), 0.03 * p)

out /= np.abs(out).max() / 0.85
pcm = (out.T * 32767).astype(np.int16)
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
print('seconds', round(N / SR, 1))
