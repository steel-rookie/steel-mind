"""압연 공장 배경음 (직접 합성한 오리지널 루프). 80BPM, D단조, 16마디 = 48초, 끊김 없이 반복."""
import sys, wave
import numpy as np

SR = 32000
BPM = 80
BEAT = 60 / BPM
BARS = 16
N = int(SR * BEAT * 4 * BARS)
t_all = np.arange(N) / SR
rng = np.random.default_rng(3)

def midi(m): return 440 * 2 ** ((m - 69) / 12)

def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x); acc = 0.0
    # 1-pole, 두 번 (루프 시작값을 끝값으로 맞추려 두 바퀴)
    for _ in range(2):
        for i in range(len(x)):
            acc = (1 - a) * x[i] + a * acc; y[i] = acc
    return y

def lp_fast(x, cutoff):
    # FFT 기반 부드러운 저역 통과 (원형 = 루프에 자연스럽다)
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR)
    X *= 1 / (1 + (f / cutoff) ** 4)
    return np.fft.irfft(X, len(x))

def bp_fast(x, lo, hi):
    X = np.fft.rfft(x); f = np.fft.rfftfreq(len(x), 1 / SR)
    X *= np.exp(-((np.log(f + 1) - np.log((lo * hi) ** 0.5)) ** 2) / (2 * (np.log(hi / lo) / 2) ** 2))
    return np.fft.irfft(X, len(x))

def add(buf, start, sig):
    """원형으로 더하기(끝을 넘으면 앞으로) → 끊김 없는 루프"""
    idx = (np.arange(len(sig)) + start) % N
    np.add.at(buf, idx, sig)

out = np.zeros((2, N))

# 1) 낮은 드론 (D1 + A1), 천천히 숨쉬는 필터
drone = np.zeros(N)
for m, amp in ((26, 1.0), (33, 0.5), (38, 0.25)):
    f = midi(m)
    for det in (-0.15, 0.15):
        ph = 2 * np.pi * (f + det) * t_all
        drone += amp * (2 * ((ph / (2 * np.pi)) % 1) - 1)
lfo = 0.5 + 0.5 * np.sin(2 * np.pi * t_all / (BEAT * 16))
drone = lp_fast(drone, 220) * (0.7 + 0.3 * lfo)
out += 0.10 * drone

# 2) 공장 웅웅거림 (전기 험 + 걸러낸 잡음)
hum = 0.4 * np.sin(2 * np.pi * 60 * t_all) + 0.2 * np.sin(2 * np.pi * 120 * t_all)
air = bp_fast(rng.standard_normal(N), 150, 900)
air /= np.abs(air).max()
out[0] += 0.03 * hum + 0.05 * air
out[1] += 0.03 * hum + 0.05 * np.roll(air, SR // 7)

# 3) 패드 화음 Dm - B♭ - F - C (2마디씩)
chords = [[50, 53, 57, 62], [46, 50, 53, 58], [53, 57, 60, 65], [48, 52, 55, 60]]
bar = int(SR * BEAT * 4)
for b in range(0, BARS, 2):
    ch = chords[(b // 2) % 4]
    L = bar * 2
    tt = np.arange(L + bar) / SR
    env = np.clip(tt / 1.8, 0, 1) * np.clip((L + bar * 0.6 - tt * SR) / (bar * 0.9), 0, 1)
    sig = np.zeros(len(tt))
    for m in ch:
        for det in (-0.3, 0.3):
            sig += np.sin(2 * np.pi * (midi(m) + det) * tt + 0.3 * np.sin(2 * np.pi * 0.2 * tt))
    sig = sig * env / len(ch)
    add(out[0], b * bar, 0.055 * sig); add(out[1], b * bar + 90, 0.055 * sig)

# 4) 롤러 리듬: 둔탁한 쿵(박마다) + 금속 땡(엇박, 가끔)
def thump(L=0.35):
    tt = np.arange(int(SR * L)) / SR
    f = 55 + 60 * np.exp(-tt * 30)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt * 9)

def clank(freq, L=0.9):
    tt = np.arange(int(SR * L)) / SR
    s = sum(np.sin(2 * np.pi * freq * r * tt) * np.exp(-tt * (5 + 3 * i)) for i, r in enumerate((1, 2.76, 5.4, 8.9)))
    s += 0.3 * rng.standard_normal(len(tt)) * np.exp(-tt * 60)
    return s * 0.35

beats = BARS * 4
for k in range(beats):
    add(out[0], int(k * BEAT * SR), 0.16 * thump()); add(out[1], int(k * BEAT * SR), 0.16 * thump())
    if k % 4 == 1 or (k % 8 == 6):
        c = clank(rng.choice([392, 466, 523]))
        pan = rng.uniform(0.25, 0.75)
        st = int((k + 0.5) * BEAT * SR)
        add(out[0], st, 0.10 * (1 - pan) * c); add(out[1], st, 0.10 * pan * c)
    # 8분음 하이햇 같은 증기 칙
    for half in (0, 0.5):
        tt = np.arange(int(SR * 0.08)) / SR
        h = bp_fast(np.pad(rng.standard_normal(len(tt)), (0, 0)), 3000, 9000)[:len(tt)] if False else rng.standard_normal(len(tt))
        h = h * np.exp(-tt * 70) * (0.5 if half else 0.25)
        st = int((k + half) * BEAT * SR)
        add(out[0], st, 0.025 * h); add(out[1], st + 40, 0.025 * h)

# 5) 드문 멜로디 (D단조 펜타토닉, 종 같은 소리) - 뒤쪽 8마디만
scale = [62, 65, 67, 69, 72, 74]
pattern = [(8, 0, 2), (8, 2.5, 4), (9, 1, 3), (10, 0, 1), (10, 3, 0), (11, 2, 2),
           (12, 0, 5), (12, 1.5, 4), (13, 2, 3), (14, 0, 2), (14, 2.5, 1), (15, 1, 0)]
for b, beat, note in pattern:
    tt = np.arange(int(SR * 2.4)) / SR
    f = midi(scale[note])
    s = (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * 2 * f * tt) + 0.1 * np.sin(2 * np.pi * 3.01 * f * tt)) * np.exp(-tt * 1.8)
    s *= np.clip(tt / 0.01, 0, 1)
    st = int((b * 4 + beat) * BEAT * SR)
    add(out[0], st, 0.06 * s); add(out[1], st + 300, 0.05 * s)

# 6) 간단한 리버브(원형 지연 여러 개) → 꼬리가 루프 앞으로 이어진다
wet = np.zeros_like(out)
for d, g in ((0.113, 0.35), (0.197, 0.28), (0.281, 0.22), (0.433, 0.16), (0.617, 0.11)):
    s = int(d * SR)
    wet[0] += g * np.roll(out[1], s); wet[1] += g * np.roll(out[0], s + 37)
out = out + 0.6 * lp_fast(wet[0], 3500)[None, :] * np.array([[1], [0]]) + 0.6 * lp_fast(wet[1], 3500)[None, :] * np.array([[0], [1]])

out /= np.abs(out).max() / 0.8
pcm = (out.T * 32767).astype(np.int16)
with wave.open(sys.argv[1], 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
print('seconds', N / SR)
