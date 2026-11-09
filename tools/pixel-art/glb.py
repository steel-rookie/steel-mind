"""Minimal GLB reader: returns per-node world-space triangles with material ids."""
import json, struct
import numpy as np

CT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
NC = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

def load(path):
    b = open(path, 'rb').read()
    clen, _ = struct.unpack('<II', b[12:20])
    j = json.loads(b[20:20 + clen])
    off = 20 + clen
    blen, _ = struct.unpack('<II', b[off:off + 8])
    bin_ = b[off + 8:off + 8 + blen]
    return j, bin_

def accessor(j, bin_, i):
    a = j['accessors'][i]
    bv = j['bufferViews'][a['bufferView']]
    dt = np.dtype(CT[a['componentType']])
    n = NC[a['type']]
    start = bv.get('byteOffset', 0) + a.get('byteOffset', 0)
    stride = bv.get('byteStride', 0)
    if stride and stride != dt.itemsize * n:
        raw = np.frombuffer(bin_, np.uint8, count=stride * (a['count'] - 1) + dt.itemsize * n, offset=start)
        out = np.stack([np.frombuffer(raw[k * stride:k * stride + dt.itemsize * n].tobytes(), dt) for k in range(a['count'])])
    else:
        out = np.frombuffer(bin_, dt, count=a['count'] * n, offset=start).reshape(a['count'], n)
    out = out.astype(np.float64)
    if a.get('normalized'):
        out = out / np.iinfo(dt).max
    return out

def trs(n):
    if 'matrix' in n:
        return np.array(n['matrix'], dtype=np.float64).reshape(4, 4).T
    t = np.array(n.get('translation', [0, 0, 0]), float)
    x, y, z, w = n.get('rotation', [0, 0, 0, 1])
    s = np.array(n.get('scale', [1, 1, 1]), float)
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * s
    m[:3, 3] = t
    return m

def triangles(path):
    """Yield (top_level_name, node_name, material_index, tris[N,3,3] world space)."""
    j, bin_ = load(path)
    nodes = j['nodes']
    parent = {}
    for i, n in enumerate(nodes):
        for c in n.get('children', []):
            parent[c] = i
    out = []
    def walk(i, m, tag):
        n = nodes[i]
        m = m @ trs(n)
        name = n.get('name') or f'node{i}'
        if 'mesh' in n:
            for p in j['meshes'][n['mesh']]['primitives']:
                if p.get('mode', 4) != 4:
                    continue
                pos = accessor(j, bin_, p['attributes']['POSITION'])
                pos = (np.c_[pos, np.ones(len(pos))] @ m.T)[:, :3]
                idx = accessor(j, bin_, p['indices']).astype(int).ravel() if 'indices' in p else np.arange(len(pos))
                out.append((tag, name, p.get('material', -1), pos[idx.reshape(-1, 3)]))
        for c in n.get('children', []):
            walk(c, m, tag)
    roots = j['scenes'][j.get('scene', 0)]['nodes']
    def tag_walk(i, m, tag):
        n = nodes[i]
        name = n.get('name') or ''
        if name.startswith('EQ_') or tag is None and name in ('Floor_Slab', 'Operator_Walkways', 'Roller_Tables', 'Hot_Material.001', 'Hot_Material'):
            tag = name
        n2 = dict(n); kids = n.get('children', [])
        m2 = m @ trs(n)
        if 'mesh' in n:
            for p in j['meshes'][n['mesh']]['primitives']:
                if p.get('mode', 4) != 4:
                    continue
                pos = accessor(j, bin_, p['attributes']['POSITION'])
                pos = (np.c_[pos, np.ones(len(pos))] @ m2.T)[:, :3]
                idx = accessor(j, bin_, p['indices']).astype(int).ravel() if 'indices' in p else np.arange(len(pos))
                out.append((tag or name, name, p.get('material', -1), pos[idx.reshape(-1, 3)]))
        for c in kids:
            tag_walk(c, m2, tag)
    for r in roots:
        tag_walk(r, np.eye(4), None)
    return j, out
