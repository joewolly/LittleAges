"""Tiny isometric SVG painter for Little Ages mockups (Clash-style chunky lit shapes)."""
import math

def _hex(c):
    c = c.lstrip('#'); return [int(c[i:i+2], 16) for i in (0, 2, 4)]

def shade(c, f):
    r = _hex(c)
    if f >= 0:
        r = [v + (255 - v) * f for v in r]
    else:
        r = [v * (1 + f) for v in r]
    return '#%02x%02x%02x' % tuple(max(0, min(255, int(round(v)))) for v in r)

def hull(pts):
    pts = sorted(set((round(x, 2), round(y, 2)) for x, y in pts))
    if len(pts) < 3: return pts
    def cross(o, a, b): return (a[0]-o[0])*(b[1]-o[1]) - (a[1]-o[1])*(b[0]-o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0: lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0: up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]

def lerp(a, b, t): return tuple(a[i] + (b[i]-a[i])*t for i in range(len(a)))

OUT = '#2b1a10'

class Canvas:
    def __init__(self, S=40, prefix='s'):
        self.snow = False
        self.S = S; self.items = []; self.defs = []; self.n = 0; self.prefix = prefix
        self.bx = [1e9, 1e9, -1e9, -1e9]
        self.defs.append(f'<filter id="{prefix}-blur" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="{S*0.09:.1f}"/></filter>')
        self.defs.append(f'<filter id="{prefix}-glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="{S*0.2:.1f}"/></filter>')

    # projection ---------------------------------------------------------
    def P(self, x, y, z=0.0):
        return ((x - y) * self.S, (x + y) * self.S / 2 - z * self.S)

    def _track(self, pts, pad=0):
        for x, y in pts:
            self.bx[0] = min(self.bx[0], x - pad); self.bx[1] = min(self.bx[1], y - pad)
            self.bx[2] = max(self.bx[2], x + pad); self.bx[3] = max(self.bx[3], y + pad)

    def _pts(self, pts): return ' '.join(f'{x:.1f},{y:.1f}' for x, y in pts)

    def grad(self, stops, vertical=False, radial=False):
        self.n += 1; gid = f'{self.prefix}-g{self.n}'
        st = ''.join(f'<stop offset="{o}" stop-color="{c}"/>' for o, c in stops)
        if radial:
            self.defs.append(f'<radialGradient id="{gid}" cx="0.35" cy="0.3" r="0.8">{st}</radialGradient>')
        elif vertical:
            self.defs.append(f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">{st}</linearGradient>')
        else:
            self.defs.append(f'<linearGradient id="{gid}" x1="0" y1="0" x2="1" y2="0">{st}</linearGradient>')
        return f'url(#{gid})'

    # raw drawing ---------------------------------------------------------
    def poly2(self, pts, fill, stroke='auto', sw=1.0, op=None, extra=''):
        self._track(pts)
        if stroke == 'auto': stroke = shade(fill, -0.45) if fill.startswith('#') else OUT
        s = f' stroke="{stroke}" stroke-width="{sw*self.S/40:.2f}" stroke-linejoin="round"' if stroke else ''
        o = f' opacity="{op}"' if op is not None else ''
        self.items.append(f'<polygon points="{self._pts(pts)}" fill="{fill}"{s}{o}{extra}/>')

    def poly(self, pts3, fill, **kw): self.poly2([self.P(*p) for p in pts3], fill, **kw)

    def line(self, a, b, color, w=1.0, op=1.0, cap='round'):
        pa, pb = self.P(*a), self.P(*b)
        self.items.append(f'<line x1="{pa[0]:.1f}" y1="{pa[1]:.1f}" x2="{pb[0]:.1f}" y2="{pb[1]:.1f}" stroke="{color}" stroke-width="{w*self.S/40:.2f}" stroke-linecap="{cap}" opacity="{op}"/>')

    def path(self, d, fill='none', stroke=None, sw=1.0, op=1.0, extra=''):
        import re
        nums = [] if 'A' in d else [float(v) for v in re.findall(r'-?\d+\.?\d*', d)]
        self._track(list(zip(nums[0::2], nums[1::2])))
        s = f' stroke="{stroke}" stroke-width="{sw*self.S/40:.2f}" stroke-linecap="round" stroke-linejoin="round"' if stroke else ''
        self.items.append(f'<path d="{d}" fill="{fill}"{s} opacity="{op}"{extra}/>')

    def circle2(self, c, r, fill, stroke=None, sw=1.0, op=1.0, extra=''):
        self._track([(c[0]-r, c[1]-r), (c[0]+r, c[1]+r)])
        s = f' stroke="{stroke}" stroke-width="{sw*self.S/40:.2f}"' if stroke else ''
        self.items.append(f'<circle cx="{c[0]:.1f}" cy="{c[1]:.1f}" r="{r:.1f}" fill="{fill}"{s} opacity="{op}"{extra}/>')

    # primitives ------------------------------------------------------------
    def shadow(self, cx, cy, rx, ry=None, op=0.28, z=0.0):
        ry = rx if ry is None else ry
        pts = [self.P(cx + rx*math.cos(t), cy + ry*math.sin(t), z) for t in [i*math.pi/16 for i in range(32)]]
        self._track(pts)
        self.items.append(f'<polygon points="{self._pts(pts)}" fill="#1d2a10" opacity="{op}" filter="url(#{self.prefix}-blur)"/>')

    def tile(self, x, y, col, z=0.0, stroke=None, w=1, d=1):
        self.poly([(x, y, z), (x+w, y, z), (x+w, y+d, z), (x, y+d, z)], col, stroke=stroke)

    def face_lines(self, A, B, C, D, color, rows=0, cols=0, stagger=False, w=0.8, op=0.5):
        """A-B bottom edge, D-C top edge."""
        for i in range(1, rows):
            v = i/rows; self.line(lerp(A, D, v), lerp(B, C, v), color, w, op)
        if cols:
            for r in range(max(rows, 1)):
                v0, v1 = r/max(rows, 1), (r+1)/max(rows, 1)
                off = (0.5/cols if (stagger and r % 2) else 0)
                for j in range(cols):
                    u = j/cols + off
                    if u <= 0.02 or u >= 0.98: continue
                    self.line(lerp(lerp(A, B, u), lerp(D, C, u), v0), lerp(lerp(A, B, u), lerp(D, C, u), v1), color, w, op)

    def box(self, x, y, z, w, d, h, col, top=None, left=None, right=None, pat=None, patc=None, sw=1.0, rows=None, cols=None):
        top = top or shade(col, 0.2); left = left or col; right = right or shade(col, -0.28)
        # left-front face (y = y+d)
        L = [(x, y+d, z), (x+w, y+d, z), (x+w, y+d, z+h), (x, y+d, z+h)]
        R = [(x+w, y+d, z), (x+w, y, z), (x+w, y, z+h), (x+w, y+d, z+h)]
        T = [(x, y, z+h), (x+w, y, z+h), (x+w, y+d, z+h), (x, y+d, z+h)]
        self.poly(L, left, sw=sw); self.poly(R, right, sw=sw); self.poly(T, top, sw=sw)
        if pat:
            pc = patc or shade(col, -0.45)
            if pat == 'stone':
                rr = rows or max(2, round(h/0.22)); cl = cols or max(2, round(w/0.4)); cr = cols or max(2, round(d/0.4))
                self.face_lines(*L, pc, rows=rr, cols=cl, stagger=True, op=0.55)
                self.face_lines(*R, pc, rows=rr, cols=cr, stagger=True, op=0.55)
                # chunky highlight on top edge of stones
                self.line(L[3], L[2], shade(col, 0.45), 1.2, 0.8)
            elif pat == 'plank':
                self.face_lines(*L, pc, cols=cols or max(2, round(w/0.18)), op=0.5)
                self.face_lines(*R, pc, cols=cols or max(2, round(d/0.18)), op=0.5)
            elif pat == 'hplank':
                self.face_lines(*L, pc, rows=rows or max(2, round(h/0.14)), op=0.5)
                self.face_lines(*R, pc, rows=rows or max(2, round(h/0.14)), op=0.5)
            elif pat == 'deck':
                n = cols or max(2, round(w/0.2))
                for i in range(1, n):
                    u = i/n; self.line(lerp(T[0], T[1], u), lerp(T[3], T[2], u), pc, 0.8, 0.45)
        return L, R, T

    def circ_pts(self, cx, cy, z, r, n=28, ry=None):
        ry = r if ry is None else ry
        return [(cx + r*math.cos(2*math.pi*i/n), cy + ry*math.sin(2*math.pi*i/n), z) for i in range(n)]

    def cyl(self, cx, cy, z, r, h, col, top=None, lines=0, linec=None, rings=0, capcol=None):
        bot = [self.P(*p) for p in self.circ_pts(cx, cy, z, r)]
        tp = [self.P(*p) for p in self.circ_pts(cx, cy, z+h, r)]
        g = self.grad([(0, shade(col, 0.22)), (0.45, col), (1, shade(col, -0.35))])
        self.poly2(hull(bot + tp), g, stroke=shade(col, -0.5))
        if lines:
            for i in range(lines):
                t = math.pi*0.25 + math.pi*(i+0.5)/lines  # front half, facing viewer (+x+y)
                a = (cx + r*math.cos(t - math.pi/2 + math.pi/4 - math.pi/4), cy + r*math.sin(t), z)
                ang = math.pi/4 + (i+0.5)/lines*math.pi - math.pi/2
                p0 = (cx + r*math.cos(ang), cy + r*math.sin(ang), z)
                self.line(p0, (p0[0], p0[1], z+h), linec or shade(col, -0.45), 0.8, 0.5)
        for i in range(1, rings+1):
            zz = z + h*i/(rings+1)
            pts = [self.P(*p) for p in self.circ_pts(cx, cy, zz, r, 28)]
            front = [p for k, p in enumerate(pts) if math.sin(2*math.pi*k/28 - math.pi/4) >= -0.05]
        if top is not False:
            self.poly2(tp, top or shade(col, 0.15), stroke=shade(col, -0.5))

    def cone(self, cx, cy, z, r, h, col, thatch=0, tc=None, eave=True):
        base = [self.P(*p) for p in self.circ_pts(cx, cy, z, r, 36)]
        apex = self.P(cx, cy, z+h)
        g = self.grad([(0, shade(col, 0.28)), (0.4, col), (1, shade(col, -0.38))])
        self.poly2(hull(base + [apex]), g, stroke=shade(col, -0.5))
        if thatch:
            for i in range(thatch):
                ang = math.pi/4 - math.pi/2 + math.pi*(i+0.5)/thatch
                p = (cx + r*math.cos(ang), cy + r*math.sin(ang), z)
                self.line(lerp((cx, cy, z+h), p, 0.18), p, tc or shade(col, -0.35), 0.9, 0.55)
            # layered thatch bands
            for f in (0.45, 0.72):
                pts = []
                for i in range(19):
                    ang = math.pi/4 - math.pi/2 + math.pi*i/18
                    pts.append(self.P(cx + r*f*math.cos(ang), cy + r*f*math.sin(ang), z + h*(1-f)))
                d = 'M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in pts)
                self.path(d, stroke=shade(col, -0.3), sw=1.4, op=0.55)
        if eave:
            pts = []
            for i in range(19):
                ang = math.pi/4 - math.pi/2 + math.pi*i/18
                pts.append(self.P(cx + r*math.cos(ang), cy + r*math.sin(ang), z))
            d = 'M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in pts)
            self.path(d, stroke=shade(col, -0.5), sw=2.2, op=0.8)
        if self.snow:
            f = 0.55
            ring = []
            for i in range(37):
                ang = 2*math.pi*i/36
                ff = f * (0.85 + 0.15*(i % 2))
                ring.append(self.P(cx + r*ff*math.cos(ang), cy + r*ff*math.sin(ang), z + h*(1-ff)))
            g2 = self.grad([(0, '#ffffff'), (0.6, '#eef4fb'), (1, '#c9d8ea')])
            self.poly2(hull(ring + [apex]), g2, stroke='#b9cbe0', sw=1.0)

    def log(self, a, b, r, col, end='#e9c48a'):
        ax = [b[i]-a[i] for i in range(3)]; L = math.sqrt(sum(v*v for v in ax)); ax = [v/L for v in ax]
        up = (0, 0, 1) if abs(ax[2]) < 0.9 else (1, 0, 0)
        u = [ax[1]*up[2]-ax[2]*up[1], ax[2]*up[0]-ax[0]*up[2], ax[0]*up[1]-ax[1]*up[0]]
        n = math.sqrt(sum(v*v for v in u)); u = [v/n for v in u]
        v = [ax[1]*u[2]-ax[2]*u[1], ax[2]*u[0]-ax[0]*u[2], ax[0]*u[1]-ax[1]*u[0]]
        def ring(c):
            return [tuple(c[i] + r*(math.cos(t)*u[i] + math.sin(t)*v[i]) for i in range(3)) for t in [2*math.pi*k/20 for k in range(20)]]
        ra, rb = ring(a), ring(b)
        g = self.grad([(0, shade(col, 0.25)), (1, shade(col, -0.35))], vertical=True)
        self.poly2(hull([self.P(*p) for p in ra + rb]), g, stroke=shade(col, -0.5))
        vis = b if (ax[0] + ax[1] + ax[2]) > 0 else a
        cap = rb if vis is b else ra
        self.poly2([self.P(*p) for p in cap], end, stroke=shade(end, -0.45))
        c = self.P(*vis)
        self.circle2(c, r*self.S*0.45, 'none', stroke=shade(end, -0.3), sw=0.9)

    def blob(self, x, y, z, r, col, hi=0.35):
        c = self.P(x, y, z)
        g = self.grad([(0, shade(col, hi)), (0.55, col), (1, shade(col, -0.4))], radial=True)
        self.circle2(c, r*self.S, g, stroke=shade(col, -0.5), sw=1.0)


    def snow_cap(self, D, C, A, B, depth=0.5, n=10):
        """White cap hanging from the ridge edge D-C down a slope toward A-B, with a scalloped lower edge."""
        top = [self.P(*D), self.P(*C)]
        low = []
        for i in range(n, -1, -1):
            u = i/n
            f = depth * (0.82 + 0.18*((i % 2) * 1.0))
            low.append(self.P(*lerp(lerp(D, A, f), lerp(C, B, f), u)))
        self.poly2(top + low, '#f7fbff', stroke='#b9cbe0', sw=1.2)

    def gable_x(self, x, y, z, w, d, rh, col, wall, o=0.12, pat='tile'):
        """Ridge along x. Front slope faces +y (screen left, lit). Gable end at x+w (screen right)."""
        m = y + d/2
        back = [(x-o, y-o, z), (x+w+o, y-o, z), (x+w+o, m, z+rh), (x-o, m, z+rh)]
        front = [(x-o, y+d+o, z), (x+w+o, y+d+o, z), (x+w+o, m, z+rh), (x-o, m, z+rh)]
        self.poly(back, shade(col, -0.15))
        self.poly([(x+w, y, z), (x+w, y+d, z), (x+w, m, z+rh)], shade(wall, -0.22))
        X2 = x+w+o
        self.poly([(X2, y-o, z), (X2, m, z+rh), (X2, y+d+o, z), (X2, y+d+o, z-0.1), (X2, m, z+rh-0.13), (X2, y-o, z-0.1)], shade(col, -0.45))
        self.poly(front, col)
        self.poly([(x-o, y+d+o, z), (x+w+o, y+d+o, z), (x+w+o, y+d+o, z-0.1), (x-o, y+d+o, z-0.1)], shade(col, -0.35))
        A, B, C, D = front[0], front[1], front[2], front[3]
        if pat == 'tile':
            self.face_lines(A, B, C, D, shade(col, -0.4), rows=5, cols=round((w+2*o)/0.16), stagger=True, op=0.45)
        elif pat == 'thatch':
            self.face_lines(A, B, C, D, shade(col, -0.35), rows=3, op=0.5, w=1.6)
            self.face_lines(A, B, C, D, shade(col, -0.25), cols=round((w+2*o)/0.07), op=0.35)
        elif pat == 'slate':
            self.face_lines(A, B, C, D, shade(col, -0.45), rows=6, cols=round((w+2*o)/0.22), stagger=True, op=0.5)
        self.line(D, C, shade(col, 0.35), 2.2, 0.9)
        if self.snow: self.snow_cap(D, C, A, B, 0.55, max(6, round(w*6)))

    def gable_y(self, x, y, z, w, d, rh, col, wall, o=0.12, pat='tile'):
        """Ridge along y. Front slope faces +x (screen right, shaded). Gable end at y+d (screen left, lit)."""
        m = x + w/2
        back = [(x-o, y-o, z), (x-o, y+d+o, z), (m, y+d+o, z+rh), (m, y-o, z+rh)]
        front = [(x+w+o, y+d+o, z), (x+w+o, y-o, z), (m, y-o, z+rh), (m, y+d+o, z+rh)]
        self.poly(back, shade(col, 0.1))
        self.poly([(x, y+d, z), (x+w, y+d, z), (m, y+d, z+rh)], wall)
        Y2 = y+d+o
        self.poly([(x-o, Y2, z), (m, Y2, z+rh), (x+w+o, Y2, z), (x+w+o, Y2, z-0.1), (m, Y2, z+rh-0.13), (x-o, Y2, z-0.1)], shade(col, -0.3))
        fc = shade(col, -0.18)
        self.poly(front, fc)
        self.poly([(x+w+o, y+d+o, z), (x+w+o, y-o, z), (x+w+o, y-o, z-0.1), (x+w+o, y+d+o, z-0.1)], shade(col, -0.45))
        A, B, C, D = front
        if pat == 'tile':
            self.face_lines(A, B, C, D, shade(fc, -0.4), rows=5, cols=round((d+2*o)/0.16), stagger=True, op=0.45)
        elif pat == 'thatch':
            self.face_lines(A, B, C, D, shade(fc, -0.35), rows=3, op=0.5, w=1.6)
            self.face_lines(A, B, C, D, shade(fc, -0.25), cols=round((d+2*o)/0.07), op=0.35)
        elif pat == 'slate':
            self.face_lines(A, B, C, D, shade(fc, -0.45), rows=6, cols=round((d+2*o)/0.22), stagger=True, op=0.5)
        self.line(D, C, shade(col, 0.35), 2.2, 0.9)
        if self.snow: self.snow_cap(D, C, A, B, 0.55, max(6, round(d*6)))

    def pyramid(self, x, y, z, w, d, rh, col, pat='thatch', o=0.12):
        ap = (x + w/2, y + d/2, z + rh)
        c = [(x-o, y-o, z), (x+w+o, y-o, z), (x+w+o, y+d+o, z), (x-o, y+d+o, z)]
        self.poly([c[0], c[1], ap], shade(col, 0.05))
        self.poly([c[3], c[0], ap], shade(col, 0.12))
        self.poly([c[2], c[3], ap], col)                  # +y face (left, lit)
        self.poly([c[1], c[2], ap], shade(col, -0.22))    # +x face (right)
        for (a, b, cc) in ((c[3], c[2], col), (c[2], c[1], shade(col, -0.22))):
            n = 9
            for i in range(1, n):
                p = lerp(a, b, i/n); self.line(lerp(p, ap, 0.05), lerp(p, ap, 0.85), shade(cc, -0.35), 0.9, 0.45)
            for f in (0.35, 0.65):
                self.line(lerp(a, ap, f), lerp(b, ap, f), shade(cc, -0.35), 1.5, 0.5)

    def door(self, face, u0, u1, v1, col='#3a2414', frame='#8a5a32', arch=True):
        A, B, C, D = face
        p0, p1 = lerp(A, B, u0), lerp(A, B, u1)
        q0, q1 = lerp(p0, lerp(D, C, u0), v1), lerp(p1, lerp(D, C, u1), v1)
        pts = [self.P(*p0), self.P(*p1), self.P(*q1)]
        if arch:
            mid = lerp(q0, q1, 0.5); top = (mid[0], mid[1], mid[2] + 0.12)
            pts += [self.P(*lerp(q1, top, 0.6)), self.P(*top), self.P(*lerp(q0, top, 0.6))]
        pts += [self.P(*q0)]
        self.poly2(pts, frame, stroke=shade(frame, -0.5), sw=3.2)
        self.poly2(pts, col, stroke=None)

    def window(self, face, u0, u1, v0, v1, glow='#ffd36b', frame='#6b4426'):
        A, B, C, D = face
        def at(u, v): return lerp(lerp(A, B, u), lerp(D, C, u), v)
        pts = [self.P(*at(u0, v0)), self.P(*at(u1, v0)), self.P(*at(u1, v1)), self.P(*at(u0, v1))]
        self.poly2(pts, glow, stroke=frame, sw=3.0)
        self.line(at((u0+u1)/2, v0), at((u0+u1)/2, v1), frame, 1.6)
        self.line(at(u0, (v0+v1)/2), at(u1, (v0+v1)/2), frame, 1.6)

    def svg(self, pad=6, width=None, height=None, extra_attr=''):
        x0, y0, x1, y1 = self.bx
        x0 -= pad; y0 -= pad; x1 += pad; y1 += pad
        vw, vh = x1 - x0, y1 - y0
        wa = f' width="{width}"' if width else ''
        ha = f' height="{height}"' if height else ''
        return (f'<svg viewBox="{x0:.0f} {y0:.0f} {vw:.0f} {vh:.0f}"{wa}{ha} xmlns="http://www.w3.org/2000/svg"{extra_attr}>'
                f'<defs>{"".join(self.defs)}</defs>{"".join(self.items)}</svg>')
