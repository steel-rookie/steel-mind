"""Top-down oblique rasterizer (ground seen from above, vertical faces seen from the front, both 1:1)."""
import numpy as np

def render(tris_list, W, H, to_px, light=(-0.35, 0.85, 0.45)):
    depth = np.full((H, W), -1e9); lam = np.zeros((H, W)); up = np.zeros((H, W)); front = np.zeros((H, W))
    mat = np.full((H, W), -1, int); gid = np.full((H, W), -1, int)
    L = np.array(light, float); L /= np.linalg.norm(L)
    V = np.array([0, 1, 1.0]) / np.sqrt(2)
    for item in tris_list:
        m, tris = item[0], item[1]; g = item[2] if len(item) > 2 else 0
        sx, sy = to_px(tris[..., 0], tris[..., 1], tris[..., 2])
        dz = tris[..., 1] + tris[..., 2]
        n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
        nl = np.linalg.norm(n, axis=1, keepdims=True); nl[nl == 0] = 1; n = n / nl
        n = np.where((n @ V)[:, None] < 0, -n, n)
        li = np.clip(n @ L, 0, 1)
        for k in range(len(tris)):
            px, py, pz = sx[k], sy[k], dz[k]
            xmin = max(int(np.floor(px.min())), 0); xmax = min(int(np.ceil(px.max())), W - 1)
            ymin = max(int(np.floor(py.min())), 0); ymax = min(int(np.ceil(py.max())), H - 1)
            if xmin > xmax or ymin > ymax:
                continue
            gx, gy = np.meshgrid(np.arange(xmin, xmax + 1) + 0.5, np.arange(ymin, ymax + 1) + 0.5)
            (x1, x2, x3), (y1, y2, y3) = px, py
            d = (y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)
            if abs(d) < 1e-12:
                continue
            w1 = ((y2 - y3) * (gx - x3) + (x3 - x2) * (gy - y3)) / d
            w2 = ((y3 - y1) * (gx - x3) + (x1 - x3) * (gy - y3)) / d
            w3 = 1 - w1 - w2
            inside = (w1 >= -1e-9) & (w2 >= -1e-9) & (w3 >= -1e-9)
            if not inside.any():
                continue
            zz = w1 * pz[0] + w2 * pz[1] + w3 * pz[2]
            sub = depth[ymin:ymax + 1, xmin:xmax + 1]
            win = inside & (zz > sub)
            if not win.any():
                continue
            sub[win] = zz[win]
            for buf, val in ((lam, li[k]), (up, n[k, 1]), (front, n[k, 2]), (mat, m), (gid, g)):
                buf[ymin:ymax + 1, xmin:xmax + 1][win] = val
    return dict(depth=depth, lam=lam, up=up, front=front, mat=mat, gid=gid)
