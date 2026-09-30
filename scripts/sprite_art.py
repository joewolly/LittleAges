"""Original Little Ages isometric sprite art: painted, lit, chunky shapes drawn as SVG."""
import math
from sprite_iso import Canvas, shade, lerp, hull

STONE = '#b3aa9b'; STONE_D = '#8e877c'; WOOD = '#a0643a'; WOOD_D = '#6e4024'; WOOD_L = '#cf9a5e'
THATCH = '#e3b24c'; CLAY = '#d25a3c'; PLASTER = '#f3e4c4'; SLATE = '#5d7a93'
SEASON = 'spring'
TEAL = '#1f8a7e'; GOLD = '#f5c451'; GRASS = '#86c64a'; DIRT = '#c99b5f'; LEAF = '#5fae3a'; PINE = '#2f7d4a'

def dirt_pad(c, X, Y, w, d, col=DIRT):
    c.poly([(X-0.1, Y-0.1, 0), (X+w+0.1, Y-0.1, 0), (X+w+0.1, Y+d+0.1, 0), (X-0.1, Y+d+0.1, 0)], col, stroke=shade(col, -0.2))

def plinth(c, X, Y, w, d, h=0.22, col=STONE):
    c.shadow(X + w/2 + 0.15, Y + d/2 + 0.15, w*0.62, d*0.62, op=0.3)
    return c.box(X, Y, 0, w, d, h, col, pat='stone', rows=2)

def banner(c, x, y, z, h=1.0, col=TEAL, emblem=GOLD):
    c.box(x-0.03, y-0.03, z, 0.06, 0.06, h, WOOD_D)
    c.blob(x, y, z+h+0.05, 0.07, GOLD, hi=0.5)
    # flag hangs toward +y (screen left) from the pole
    pts = [(x, y+0.04, z+h-0.05), (x, y+0.5, z+h-0.05), (x, y+0.5, z+h-0.55), (x, y+0.27, z+h-0.42), (x, y+0.04, z+h-0.55)]
    c.poly(pts, col, sw=1.4)
    c.blob(x, y+0.27, z+h-0.22, 0.07, emblem, hi=0.4)

def smoke(c, x, y, z):
    for i, (dx, dz, r) in enumerate([(0, 0.15, 0.1), (-0.08, 0.35, 0.13), (-0.02, 0.6, 0.16)]):
        p = c.P(x + dx, y - dx, z + dz)
        c.circle2(p, r*c.S, '#f4f1ea', stroke='#c9c2b5', sw=0.8, op=0.85 - i*0.2)

def villager(c, x, y, cloth='#3d7fc4', skin='#f0c090', hair='#5a3620', carry=None, s=1.0):
    c.shadow(x, y, 0.16*s, 0.16*s, op=0.35)
    # legs
    for dy in (-0.05, 0.05):
        c.box(x-0.03, y+dy-0.03, 0, 0.06*s, 0.06*s, 0.16*s, '#5b4032')
    # body (tunic) as a small cone-ish cylinder
    c.cyl(x, y, 0.14*s, 0.11*s, 0.2*s, cloth, top=shade(cloth, 0.1))
    c.blob(x, y, 0.34*s, 0.05*s, cloth)
    # head
    h = c.P(x, y, 0.5*s)
    g = c.grad([(0, shade(skin, 0.35)), (0.6, skin), (1, shade(skin, -0.3))], radial=True)
    c.circle2(h, 0.13*s*c.S, g, stroke=shade(skin, -0.5), sw=1.0)
    c.path(f'M{h[0]-0.13*s*c.S:.1f},{h[1]-0.01*c.S:.1f} A{0.13*s*c.S:.1f},{0.13*s*c.S:.1f} 0 0 1 {h[0]+0.13*s*c.S:.1f},{h[1]-0.01*c.S:.1f} Q{h[0]:.1f},{h[1]-0.09*s*c.S:.1f} {h[0]-0.13*s*c.S:.1f},{h[1]-0.01*c.S:.1f}Z', fill=hair, stroke=shade(hair, -0.4), sw=0.8)
    for ex in (-0.045, 0.02):
        c.circle2((h[0] + ex*c.S*s, h[1] + 0.02*c.S*s), 0.018*c.S*s, '#2b1a10')
    if carry == 'wood':
        c.log((x-0.18, y+0.12, 0.42*s), (x+0.2, y+0.12, 0.42*s), 0.06, WOOD)
    elif carry == 'stone':
        c.blob(x+0.05, y+0.14, 0.38*s, 0.1, '#a7a39c')
    elif carry == 'food':
        c.cyl(x+0.05, y+0.15, 0.3*s, 0.1, 0.1, '#b9854a', top='#d8453a')

# ---------------------------------------------------------------- resources
AUTUMN = ['#e58a2e', '#d4552c', '#e9b53a', '#c9772a']
def oak(c, x, y, s=1.0, col=LEAF, variant=0):
    c.shadow(x+0.25, y+0.25, 0.55*s, 0.45*s, op=0.3)
    c.cyl(x, y, 0, 0.1*s, 0.55*s, '#7a4a2a')
    if SEASON == 'winter':
        for (dx, dy, dz) in [(0.3, -0.1, 1.2), (-0.3, 0.15, 1.1), (0.05, 0.3, 1.0), (-0.05, -0.3, 1.25), (0, 0, 1.4)]:
            c.line((x, y, 0.5*s), (x+dx*s, y+dy*s, dz*s), '#6e4a30', 3.4)
            c.line((x+dx*s*0.7, y+dy*s*0.7, dz*s*0.8), (x+dx*s*1.2, y+dy*s*0.9, dz*s*1.05), '#6e4a30', 2.0)
            c.blob(x+dx*s, y+dy*s, dz*s+0.03, 0.06*s, '#f7fbff', hi=0.2)
        return
    if SEASON == 'autumn':
        col = AUTUMN[variant % len(AUTUMN)]
    elif SEASON == 'summer':
        col = ['#4e9f2c', '#5aa834', '#468f28'][variant % 3]
    else:
        col = ['#5fae3a', '#6cb842', '#559f33'][variant % 3]
    for dx, dy, dz, r, k in [(0.12, -0.18, 0.95, 0.38, -0.1), (-0.2, 0.12, 0.9, 0.36, 0.0), (0.18, 0.2, 0.75, 0.34, -0.05), (0, 0, 1.2, 0.4, 0.08)]:
        c.blob(x+dx*s, y+dy*s, dz*s, r*s, shade(col, k))
    if SEASON == 'spring':
        for (dx, dy, dz) in [(0.05, 0.2, 1.3), (-0.25, 0.1, 1.1), (0.2, -0.1, 1.15), (0.25, 0.3, 0.85), (-0.1, 0.35, 0.95), (0.0, -0.25, 1.35), (-0.3, -0.05, 0.95)]:
            c.blob(x+dx*s, y+dy*s, dz*s, 0.055*s, '#f7a8c4', hi=0.5)

def pine(c, x, y, s=1.0, col=PINE):
    c.shadow(x+0.2, y+0.2, 0.4*s, 0.35*s, op=0.3)
    c.cyl(x, y, 0, 0.07*s, 0.3*s, '#6e4024')
    for z, r, h in [(0.25, 0.45, 0.75), (0.65, 0.35, 0.65), (1.0, 0.24, 0.55)]:
        c.cone(x, y, z*s, r*s, h*s, col, eave=True)

def berry_bush(c, x, y, s=1.0):
    c.shadow(x+0.1, y+0.1, 0.4*s, 0.35*s, op=0.28)
    bc = {'winter': '#8a9a86', 'autumn': '#9a8a36'}.get(SEASON, '#4f9e36')
    for dx, dy, dz, r in [(-0.12, 0.05, 0.22, 0.24), (0.14, -0.08, 0.24, 0.24), (0.02, 0.14, 0.18, 0.22), (0, -0.02, 0.38, 0.22)]:
        c.blob(x+dx*s, y+dy*s, dz*s, r*s, bc)
    if SEASON == 'winter':
        c.blob(x, y, 0.5*s, 0.14*s, '#f7fbff', hi=0.2)
        return
    for dx, dy, dz in [(-0.2, 0.1, 0.3), (0.05, 0.2, 0.25), (0.2, 0.0, 0.35), (-0.05, -0.05, 0.52), (0.12, 0.12, 0.45), (-0.15, -0.1, 0.45)]:
        c.blob(x+dx*s, y+dy*s, dz*s, 0.055*s, '#e0364a', hi=0.6)

def rocks(c, x, y, s=1.0, col='#a9a49b'):
    c.shadow(x+0.1, y+0.1, 0.5*s, 0.4*s, op=0.3)
    def rock(cx, cy, r, h, k):
        base = [(cx + r*math.cos(a), cy + r*0.85*math.sin(a), 0) for a in [i*math.pi/3 + k for i in range(6)]]
        topp = [(cx + r*0.55*math.cos(a+0.4), cy + r*0.5*math.sin(a+0.4), h) for a in [i*math.pi/3 + k for i in range(6)]]
        c.poly2(hull([c.P(*p) for p in base + topp]), shade(col, -0.25), stroke=shade(col, -0.55))
        # lit facets
        c.poly([base[2], base[1], topp[1], topp[2]], col, stroke=shade(col, -0.45))
        c.poly([base[1], base[0], topp[0], topp[1]], shade(col, -0.12), stroke=shade(col, -0.45))
        c.poly(topp, shade(col, 0.28), stroke=shade(col, -0.4))
    rock(x-0.15, y-0.12, 0.3*s, 0.42*s, 0.2)
    rock(x+0.22, y+0.08, 0.22*s, 0.3*s, 0.7)
    rock(x-0.05, y+0.25, 0.18*s, 0.22*s, 0.1)

# ---------------------------------------------------------------- shelters (ages)
def tent(c, X, Y):
    dirt_pad(c, X+0.1, Y+0.1, 1.3, 1.3)
    cx, cy = X+0.75, Y+0.75
    c.shadow(cx+0.25, cy+0.25, 0.8, 0.7)
    for a in (0.3, 1.8, 3.4, 4.9):
        c.line((cx, cy, 1.25), (cx + 0.18*math.cos(a), cy + 0.18*math.sin(a), 1.6), WOOD_D, 3.0)
    c.cone(cx, cy, 0, 0.72, 1.35, '#c9925a', thatch=0)
    # hide seams
    for ang in (0.2, 0.9, 1.6):
        p = (cx + 0.72*math.cos(ang), cy + 0.72*math.sin(ang), 0)
        c.line(lerp((cx, cy, 1.35), p, 0.15), p, '#7b5230', 1.2, 0.7)
    # door flap
    ang = math.pi/4 + 0.35
    p0 = (cx + 0.72*math.cos(ang-0.35), cy + 0.72*math.sin(ang-0.35), 0)
    p1 = (cx + 0.72*math.cos(ang+0.35), cy + 0.72*math.sin(ang+0.35), 0)
    c.poly([p0, p1, lerp(p1, (cx, cy, 1.35), 0.55), lerp(p0, (cx, cy, 1.35), 0.55)], '#4a2c18')
    c.blob(X+1.35, Y+0.35, 0.12, 0.12, '#8b5a33')

def round_hut(c, X, Y):
    dirt_pad(c, X+0.05, Y+0.05, 1.4, 1.4)
    cx, cy = X+0.75, Y+0.75
    c.shadow(cx+0.25, cy+0.25, 0.9, 0.8)
    c.cyl(cx, cy, 0, 0.62, 0.78, '#b07a47', lines=10, linec='#7a4d2a')
    ang = math.pi/4 + 0.3
    pts = [(cx + 0.63*math.cos(ang+da), cy + 0.63*math.sin(ang+da), z) for da, z in [(-0.28, 0), (0.28, 0), (0.28, 0.5), (0, 0.6), (-0.28, 0.5)]]
    c.poly(pts, '#3a2414', stroke='#8a5a32', sw=2.4)
    c.cone(cx, cy, 0.7, 0.9, 1.05, THATCH, thatch=16)
    c.cyl(cx, cy, 1.7, 0.06, 0.12, WOOD_D)

def cottage(c, X, Y):
    plinth(c, X, Y, 1.8, 1.4, 0.18)
    L, R, T = c.box(X+0.1, Y+0.1, 0.18, 1.6, 1.2, 0.72, PLASTER)
    # timber frame
    for u in (0.02, 0.5, 0.98):
        c.line(lerp(L[0], L[1], u), lerp(L[3], L[2], u), WOOD_D, 2.6)
    c.line(lerp(L[0], L[3], 0.5), lerp(L[1], L[2], 0.5), WOOD_D, 2.2)
    for u in (0.02, 0.98):
        c.line(lerp(R[0], R[1], u), lerp(R[3], R[2], u), shade(WOOD_D, -0.2), 2.6)
    c.door(L, 0.12, 0.34, 0.8)
    c.window(L, 0.62, 0.85, 0.35, 0.72)
    c.window(R, 0.35, 0.65, 0.35, 0.72, glow='#e8b44f')
    c.gable_x(X+0.1, Y+0.1, 0.9, 1.6, 1.2, 0.75, THATCH, PLASTER, o=0.16, pat='thatch')

def longhouse(c, X, Y):
    plinth(c, X, Y, 2.2, 1.4, 0.2)
    L, R, T = c.box(X+0.1, Y+0.1, 0.2, 2.0, 1.2, 0.7, WOOD, pat='hplank', rows=6)
    c.door(L, 0.4, 0.6, 0.85, frame=WOOD_D)
    c.window(L, 0.12, 0.28, 0.4, 0.72); c.window(L, 0.72, 0.88, 0.4, 0.72)
    c.gable_x(X+0.1, Y+0.1, 0.9, 2.0, 1.2, 0.85, '#c4683e', WOOD, o=0.16, pat='tile')
    # crossed gable horns
    tip = (X+2.1+0.16, Y+0.7, 1.75)
    c.line(tip, (tip[0], tip[1]+0.25, tip[2]+0.3), WOOD_D, 3.2); c.line(tip, (tip[0], tip[1]-0.25, tip[2]+0.3), WOOD_D, 3.2)
    banner(c, X+0.05, Y+1.5, 0.0, 1.5)

def stone_house(c, X, Y):
    plinth(c, X, Y, 2.2, 1.6, 0.26, STONE_D)
    L, R, T = c.box(X+0.12, Y+0.12, 0.26, 1.96, 1.36, 0.95, '#c8bfae', pat='stone', rows=5)
    for (u0, u1) in ((0.1, 0.3), (0.66, 0.86)):
        c.window(L, u0, u1, 0.35, 0.75)
    c.door(L, 0.4, 0.58, 0.72, frame=WOOD)
    c.window(R, 0.3, 0.52, 0.35, 0.75)
    # chimney
    c.box(X+0.4, Y+0.2, 1.1, 0.3, 0.3, 1.1, STONE_D, pat='stone', rows=4, cols=2)
    c.gable_x(X+0.12, Y+0.12, 1.21, 1.96, 1.36, 0.85, SLATE, '#c8bfae', o=0.18, pat='slate')
    c.box(X+0.4, Y+0.2, 1.75, 0.3, 0.3, 0.45, STONE_D, pat='stone', rows=2, cols=2)
    c.box(X+0.37, Y+0.17, 2.2, 0.36, 0.36, 0.06, shade(STONE_D, -0.1))
    smoke(c, X+0.55, Y+0.35, 2.3)
    banner(c, X+2.4, Y+0.1, 0, 1.7)

# ---------------------------------------------------------------- other buildings
def stockpile(c, X, Y):
    c.shadow(X+1.2, Y+1.2, 1.25, 1.2)
    c.box(X, Y, 0, 2.2, 2.2, 0.16, WOOD_L, pat='deck', cols=10)
    # log stack along x, back row
    for i, (yy, zz) in enumerate([(0.35, 0.3), (0.62, 0.3), (0.89, 0.3), (0.49, 0.54), (0.76, 0.54), (0.62, 0.78)]):
        c.log((X+0.15, Y+yy, zz), (X+1.3, Y+yy, zz), 0.13, WOOD)
    # stone blocks right
    c.box(X+1.45, Y+0.2, 0.16, 0.6, 0.45, 0.35, '#b8b2a7', pat='stone', rows=2, cols=2)
    c.box(X+1.5, Y+0.25, 0.51, 0.45, 0.35, 0.3, '#aaa397', pat='stone', rows=1, cols=2)
    # crates front
    for (x, y, z) in [(X+0.25, Y+1.35, 0.16), (X+0.8, Y+1.4, 0.16), (X+0.5, Y+1.38, 0.66)]:
        L, R, T = c.box(x, y, z, 0.5, 0.5, 0.5, '#c58a4e')
        for F in (L, R):
            c.line(F[0], F[2], WOOD_D, 2.0, 0.8); c.line(F[1], F[3], WOOD_D, 2.0, 0.8)
    # sacks
    c.blob(X+1.7, Y+1.6, 0.4, 0.26, '#e2cf9e', hi=0.45)
    c.blob(X+1.85, Y+1.2, 0.35, 0.22, '#d9c38e', hi=0.45)
    # corner posts with rope
    for (x, y) in [(X+0.05, Y+0.05), (X+2.1, Y+0.05), (X+0.05, Y+2.1), (X+2.1, Y+2.1)]:
        c.box(x, y, 0.16, 0.08, 0.08, 0.35, WOOD_D)
        c.blob(x+0.04, y+0.04, 0.55, 0.06, GOLD)

def workshop(c, X, Y):
    plinth(c, X, Y, 2.2, 1.8, 0.2)
    # back wall
    c.box(X+0.15, Y+0.15, 0.2, 1.9, 0.12, 1.0, WOOD, pat='plank')
    c.box(X+0.15, Y+0.15, 0.2, 0.12, 1.5, 1.0, shade(WOOD, -0.05), pat='plank')
    # tools on wall
    for u in (0.5, 0.9, 1.3):
        c.line((X+0.3+u, Y+0.28, 0.6), (X+0.3+u, Y+0.28, 1.0), '#6d6f73', 2.2)
    # anvil + stump
    c.cyl(X+1.5, Y+1.0, 0.2, 0.2, 0.3, '#8b5a33')
    c.box(X+1.3, Y+0.93, 0.5, 0.42, 0.16, 0.12, '#55585e')
    c.box(X+1.38, Y+0.95, 0.62, 0.3, 0.12, 0.05, '#7c8088')
    # workbench
    c.box(X+0.45, Y+0.9, 0.2, 0.08, 0.08, 0.4, WOOD_D); c.box(X+1.0, Y+0.9, 0.2, 0.08, 0.08, 0.4, WOOD_D)
    c.box(X+0.4, Y+0.5, 0.6, 0.75, 0.55, 0.1, WOOD_L)
    c.log((X+0.5, Y+0.65, 0.76), (X+0.9, Y+0.65, 0.76), 0.05, WOOD)
    # grindstone
    c.log((X+0.5, Y+1.55, 0.45), (X+0.62, Y+1.55, 0.45), 0.24, '#9a958c', end='#b9b4aa')
    # posts
    for (x, y) in [(X+2.0, Y+0.2), (X+2.0, Y+1.55), (X+0.2, Y+1.55)]:
        c.box(x, y, 0.2, 0.12, 0.12, 1.05, WOOD_D)
    c.gable_y(X+0.1, Y+0.1, 1.25, 2.0, 1.6, 0.7, CLAY, WOOD, o=0.15, pat='tile')
    # sign
    c.box(X+2.25, Y+1.75, 0, 0.06, 0.06, 0.7, WOOD_D)
    c.poly([(X+2.28, Y+1.6, 0.45), (X+2.28, Y+2.1, 0.45), (X+2.28, Y+2.1, 0.75), (X+2.28, Y+1.6, 0.75)], GOLD, sw=1.4)
    c.line((X+2.28, Y+1.75, 0.6), (X+2.28, Y+1.95, 0.6), WOOD_D, 3)

def farm(c, X, Y, w=3, d=2.2, ripe=1.0, seasonal=True, fence=True, scarecrow=True):
    c.shadow(X+w/2, Y+d/2, w*0.55, d*0.55, op=0.15)
    if seasonal:
        ripe = {'summer': 1.0, 'autumn': 0.3, 'winter': 0.0}.get(SEASON, ripe)
    c.box(X, Y, 0, w, d, 0.1, '#8a5a34', top='#e8eef4' if SEASON == 'winter' else '#9a6538')
    rows = max(3, round(d/0.37))
    for i in range(rows):
        yy = Y + (i+0.5)*d/rows
        c.line((X+0.1, yy, 0.1), (X+w-0.1, yy, 0.1), '#6f4526', 3.2, 0.8)
        for j in range(int(w/0.22)):
            xx = X + 0.15 + j*0.22 + (0.08 if i % 2 else 0)
            if xx > X + w - 0.1: continue
            if ripe <= 0: continue
            base = c.P(xx, yy, 0.1)
            hgt = (0.28 + 0.06*((i*7 + j*3) % 3)) * c.S * ripe
            col = '#e8bc3c' if ripe > 0.7 else ('#c9a04a' if SEASON == 'autumn' else '#79b845')
            c.path(f'M{base[0]-3:.1f},{base[1]:.1f} Q{base[0]-4:.1f},{base[1]-hgt*0.6:.1f} {base[0]:.1f},{base[1]-hgt:.1f} Q{base[0]+4:.1f},{base[1]-hgt*0.6:.1f} {base[0]+3:.1f},{base[1]:.1f}Z', fill=col, stroke=shade(col, -0.45), sw=0.8)
    if seasonal and SEASON == 'autumn' and w >= 2:
        for (bx, by) in [(0.5, 0.5), (1.4, 1.3), (2.2, 0.6)]:
            c.log((X+bx, Y+by, 0.28), (X+bx+0.45, Y+by, 0.28), 0.18, '#e2b84a', end='#f0d27a')
    if fence:
        for i in range(int(w/0.35)+1):
            x = X + i*0.35
            c.box(x-0.03, Y+d+0.08, 0, 0.06, 0.06, 0.32, WOOD)
        c.line((X, Y+d+0.11, 0.24), (X+w, Y+d+0.11, 0.24), WOOD_L, 2.6)
        for i in range(int(d/0.35)+1):
            y = Y + i*0.35
            c.box(X+w+0.08, y-0.03, 0, 0.06, 0.06, 0.32, shade(WOOD, -0.1))
        c.line((X+w+0.11, Y, 0.24), (X+w+0.11, Y+d, 0.24), WOOD_L, 2.6)
    if scarecrow:
        sx, sy = X + w*0.55, Y + d*0.45
        c.line((sx, sy, 0.1), (sx, sy, 0.95), WOOD_D, 3.0)
        c.line((sx, sy-0.3, 0.72), (sx, sy+0.3, 0.72), WOOD_D, 2.6)
        c.cyl(sx, sy, 0.45, 0.1, 0.3, TEAL)
        c.blob(sx, sy, 0.95, 0.1, '#e9d49a')
        c.cone(sx, sy, 1.0, 0.18, 0.18, THATCH, eave=False)

def granary(c, X, Y):
    cx, cy = X+0.9, Y+0.9
    c.shadow(cx+0.2, cy+0.2, 0.95, 0.9)
    c.cyl(cx, cy, 0, 0.85, 0.2, STONE)
    for a in (0.8, 2.4, 3.9, 5.5):
        c.box(cx + 0.5*math.cos(a) - 0.07, cy + 0.5*math.sin(a) - 0.07, 0.2, 0.14, 0.14, 0.35, STONE_D, pat='stone', rows=2, cols=1)
    c.cyl(cx, cy, 0.55, 0.75, 0.9, '#c18145', lines=12, linec='#7d4b27', top=False)
    for zz in (0.62, 1.35):
        pts = [c.P(cx + 0.76*math.cos(t), cy + 0.76*math.sin(t), zz) for t in [math.pi/4 - math.pi/2 + math.pi*i/16 for i in range(17)]]
        c.path('M' + ' L'.join(f'{x:.1f},{y:.1f}' for x, y in pts), stroke='#5b3a20', sw=3.0)
    # hatch
    ang = math.pi/2 + 0.2
    pts = [(cx + 0.76*math.cos(ang+da), cy + 0.76*math.sin(ang+da), z) for da, z in [(-0.22, 0.8), (0.22, 0.8), (0.22, 1.2), (-0.22, 1.2)]]
    c.poly(pts, '#4a2c18')
    c.cone(cx, cy, 1.35, 1.0, 0.95, THATCH, thatch=18)
    c.cyl(cx, cy, 2.25, 0.07, 0.1, WOOD_D)
    c.blob(cx, cy, 2.4, 0.09, GOLD, hi=0.5)
    # ladder
    lx, ly = cx + 0.3, cy + 0.95
    c.line((lx-0.15, ly, 0), (lx-0.15, ly-0.2, 1.0), WOOD_D, 2.4); c.line((lx+0.15, ly, 0), (lx+0.15, ly-0.2, 1.0), WOOD_D, 2.4)
    for k in range(1, 6):
        f = k/6; c.line((lx-0.15, ly-0.2*f, f), (lx+0.15, ly-0.2*f, f), WOOD_L, 2.0)
    # grain sacks
    c.blob(X+0.15, Y+1.55, 0.2, 0.2, '#e8d6a4', hi=0.45)
    c.blob(X+0.45, Y+1.75, 0.18, 0.18, '#dfc890', hi=0.45)

def stall(c, x, y, stripe, goods):
    c.box(x, y, 0, 0.9, 0.7, 0.45, WOOD, pat='plank')
    c.box(x-0.03, y-0.03, 0.45, 0.96, 0.76, 0.06, WOOD_L)
    for (px, py) in [(x, y), (x+0.84, y), (x, y+0.64), (x+0.84, y+0.64)]:
        c.box(px, py, 0.45, 0.06, 0.06, 0.65, WOOD_D)
    # awning: slope toward +y (front-left)
    A, B = (x-0.1, y+0.95, 0.95), (x+1.0, y+0.95, 0.95)
    C, D = (x+1.0, y-0.05, 1.25), (x-0.1, y-0.05, 1.25)
    n = 6
    for i in range(n):
        u0, u1 = i/n, (i+1)/n
        col = stripe if i % 2 == 0 else '#fbf2df'
        c.poly([lerp(A, B, u0), lerp(A, B, u1), lerp(D, C, u1), lerp(D, C, u0)], col, sw=0.8)
    # scalloped edge
    for i in range(n):
        u = (i+0.5)/n; p = c.P(*lerp(A, B, u))
        col = stripe if i % 2 == 0 else '#fbf2df'
        c.circle2((p[0], p[1]+1), 0.09*c.S, col, stroke=shade(col, -0.45), sw=0.8)
    c.poly([B, C, (C[0], C[1], C[2]-0.08), (B[0], B[1], B[2]-0.08)], shade(stripe, -0.35))
    for i, g in enumerate(goods):
        gx, gy = x + 0.18 + (i % 3)*0.27, y + 0.2 + (i//3)*0.3
        if g == 'apple':
            for k in range(3): c.blob(gx + k*0.05, gy + 0.05*k, 0.56, 0.06, '#d93b3b', hi=0.6)
        elif g == 'bread':
            c.blob(gx, gy, 0.56, 0.09, '#d9954a', hi=0.45)
        elif g == 'cloth':
            c.log((gx-0.08, gy, 0.58), (gx+0.1, gy, 0.58), 0.07, TEAL, end='#5cc1b3')
        elif g == 'pot':
            c.cyl(gx, gy, 0.51, 0.08, 0.12, '#c0673d')

def marketplace(c, X, Y):
    c.shadow(X+1.3, Y+1.3, 1.4, 1.3, op=0.18)
    c.poly([(X, Y, 0), (X+2.6, Y, 0), (X+2.6, Y+2.6, 0), (X, Y+2.6, 0)], '#d8c8a8', stroke='#a89878')
    for i in range(1, 7):
        c.line((X + i*2.6/7, Y, 0), (X + i*2.6/7, Y+2.6, 0), '#b8a888', 0.8, 0.8)
        c.line((X, Y + i*2.6/7, 0), (X+2.6, Y + i*2.6/7, 0), '#b8a888', 0.8, 0.8)
    stall(c, X+0.2, Y+0.2, '#d8453a', ['apple', 'apple', 'bread', 'bread', 'apple', 'bread'])
    stall(c, X+1.5, Y+0.25, '#2f8f83', ['cloth', 'cloth', 'pot', 'pot', 'cloth', 'pot'])
    # well-like central barrel + crates
    c.cyl(X+1.3, Y+1.75, 0, 0.22, 0.45, '#a86b3c', lines=6, linec='#5b3a20')
    c.cyl(X+0.55, Y+2.05, 0, 0.2, 0.4, '#a86b3c', lines=6, linec='#5b3a20')
    L, R, T = c.box(X+1.85, Y+1.75, 0, 0.45, 0.45, 0.4, '#c58a4e')
    banner(c, X+2.45, Y+2.45, 0, 1.6, col='#d8453a')

def hearth(c, X, Y):
    cx, cy = X+0.7, Y+0.7
    c.poly2([c.P(*p) for p in c.circ_pts(cx, cy, 0, 0.75)], '#cdbfa3', stroke='#a89878')
    glow = c.P(cx, cy, 0.3)
    c.items.append(f'<circle cx="{glow[0]:.1f}" cy="{glow[1]:.1f}" r="{0.75*c.S:.1f}" fill="#ffb347" opacity="0.45" filter="url(#{c.prefix}-glow)"/>')
    n = 10
    ring = [(cx + 0.5*math.cos(2*math.pi*i/n), cy + 0.5*math.sin(2*math.pi*i/n)) for i in range(n)]
    ring.sort(key=lambda p: p[0] + p[1])
    back = [p for p in ring if (p[0]-cx) + (p[1]-cy) < 0]
    front = [p for p in ring if (p[0]-cx) + (p[1]-cy) >= 0]
    for (x, y) in back: c.blob(x, y, 0.08, 0.13, '#9c968b', hi=0.4)
    c.log((cx-0.3, cy-0.1, 0.1), (cx+0.3, cy+0.1, 0.1), 0.07, '#6e4024')
    c.log((cx-0.1, cy+0.3, 0.1), (cx+0.1, cy-0.3, 0.1), 0.07, '#7a4a2a')
    f = c.P(cx, cy, 0.12)
    s = c.S
    for (h, w, col) in [(0.7, 0.26, '#e8492e'), (0.52, 0.18, '#ff9a2e'), (0.32, 0.1, '#ffe06b')]:
        c.path(f'M{f[0]-w*s:.1f},{f[1]:.1f} Q{f[0]-w*s:.1f},{f[1]-h*s*0.5:.1f} {f[0]-w*s*0.15:.1f},{f[1]-h*s:.1f} Q{f[0]+w*s*0.2:.1f},{f[1]-h*s*0.55:.1f} {f[0]+w*s*0.45:.1f},{f[1]-h*s*0.75:.1f} Q{f[0]+w*s:.1f},{f[1]-h*s*0.3:.1f} {f[0]+w*s:.1f},{f[1]:.1f}Z', fill=col, stroke=shade(col, -0.35), sw=1.0)
    for (x, y) in front: c.blob(x, y, 0.08, 0.13, '#aaa397', hi=0.45)
    c.log((X+1.25, Y+0.1, 0.07), (X+1.25, Y+0.6, 0.07), 0.07, WOOD)
    c.log((X+1.38, Y+0.15, 0.07), (X+1.38, Y+0.65, 0.07), 0.07, WOOD)

def loom(c, X, Y):
    c.shadow(X+0.8, Y+0.8, 0.9, 0.8)
    c.box(X, Y, 0, 1.6, 1.4, 0.12, WOOD_L, pat='deck', cols=6)
    x0, x1, yy = X+0.3, X+1.3, Y+0.55
    for x in (x0, x1):
        c.box(x-0.05, yy-0.05, 0.12, 0.1, 0.1, 1.3, WOOD_D)
    c.log((x0, yy, 1.35), (x1, yy, 1.35), 0.06, WOOD)
    # cloth: woven band with stripes
    cloth_top, cloth_bot = 1.25, 0.55
    n = 8
    for i in range(n):
        u0, u1 = i/n, (i+1)/n
        col = [TEAL, GOLD, '#d8453a', TEAL, '#fbf2df', TEAL, GOLD, '#d8453a'][i]
        a, b = lerp((x0+0.05, yy, 0), (x1-0.05, yy, 0), u0), lerp((x0+0.05, yy, 0), (x1-0.05, yy, 0), u1)
        c.poly([(a[0], a[1], cloth_bot), (b[0], b[1], cloth_bot), (b[0], b[1], cloth_top), (a[0], a[1], cloth_top)], col, sw=0.6)
    for k in range(4):
        z = cloth_bot + (cloth_top - cloth_bot)*(k+0.5)/4
        c.line((x0+0.05, yy, z), (x1-0.05, yy, z), '#2b1a10', 0.6, 0.25)
    for i in range(10):
        x = x0 + 0.08 + i*0.09
        c.line((x, yy, 0.25), (x, yy, cloth_bot), '#efe6d2', 0.8)
    c.log((x0, yy, 0.25), (x1, yy, 0.25), 0.05, WOOD)
    # basket with yarn
    c.cyl(X+1.15, Y+1.05, 0.12, 0.22, 0.2, '#c9955a', lines=6, linec='#8a5a32')
    for dx, dy, col in [(-0.07, 0, '#d8453a'), (0.07, 0.03, TEAL), (0, -0.07, GOLD)]:
        c.blob(X+1.15+dx, Y+1.05+dy, 0.38, 0.09, col, hi=0.45)
    c.box(X+0.1, Y+0.95, 0.12, 0.45, 0.3, 0.25, WOOD, pat='plank')

def care_house(c, X, Y):
    plinth(c, X, Y, 2.0, 1.6, 0.18)
    L, R, T = c.box(X+0.12, Y+0.12, 0.18, 1.5, 1.3, 0.78, '#f7f1e3')
    for u in (0.02, 0.98):
        c.line(lerp(L[0], L[1], u), lerp(L[3], L[2], u), '#8a5a32', 2.6)
    c.door(L, 0.35, 0.62, 0.82, col='#3e6b88', frame='#8a5a32')
    c.window(L, 0.72, 0.92, 0.4, 0.75, glow='#fff1c4')
    c.window(R, 0.35, 0.65, 0.4, 0.75, glow='#fff1c4')
    c.gable_x(X+0.12, Y+0.12, 0.96, 1.5, 1.3, 0.72, '#4f8fbf', '#f7f1e3', o=0.16, pat='tile')
    # leaf sign on gable
    p = c.P(X+1.62+0.02, Y+0.77, 1.28)
    c.circle2(p, 0.13*c.S, '#fbf2df', stroke='#8a5a32', sw=1.6)
    c.path(f'M{p[0]-4:.1f},{p[1]+3:.1f} Q{p[0]-4:.1f},{p[1]-5:.1f} {p[0]+4:.1f},{p[1]-4:.1f} Q{p[0]+4:.1f},{p[1]+4:.1f} {p[0]-4:.1f},{p[1]+3:.1f}Z', fill='#4f9e36', stroke='#2e6b1f', sw=0.8)
    # herb planters
    for (x, y) in [(X+1.72, Y+0.2), (X+1.72, Y+0.75)]:
        c.box(x, y, 0, 0.3, 0.45, 0.18, WOOD, pat='hplank', rows=2)
        for k in range(3):
            c.blob(x+0.15, y+0.08+k*0.15, 0.3, 0.08, '#5fae3a')
            c.blob(x+0.12, y+0.1+k*0.15, 0.38, 0.03, '#a36fd6', hi=0.5)
    # bench
    c.box(X+0.3, Y+1.55, 0, 0.6, 0.18, 0.2, WOOD_L)

def road(c, X, Y, n=3, paved=True):
    for i in range(n):
        x = X + i
        if paved:
            c.box(x, Y, 0, 1, 1, 0.06, '#c5b9a2', top='#d7ccb6')
            for (a, b, w, d) in [(0.05, 0.05, 0.42, 0.28), (0.52, 0.05, 0.43, 0.28), (0.05, 0.38, 0.28, 0.27), (0.38, 0.38, 0.3, 0.27), (0.73, 0.38, 0.22, 0.27), (0.05, 0.7, 0.5, 0.25), (0.6, 0.7, 0.35, 0.25)]:
                c.poly([(x+a, Y+b, 0.07), (x+a+w, Y+b, 0.07), (x+a+w, Y+b+d, 0.07), (x+a, Y+b+d, 0.07)], '#e4dac6', stroke='#a89878', sw=0.8)
        else:
            c.poly([(x, Y+0.15, 0), (x+1, Y+0.1, 0), (x+1, Y+0.9, 0), (x, Y+0.85, 0)], '#d4ab70', stroke=None)

def construction(c, X, Y):
    """Under-construction variant: scaffold and half walls."""
    plinth(c, X, Y, 1.8, 1.4, 0.18)
    L, R, T = c.box(X+0.1, Y+0.1, 0.18, 1.6, 1.2, 0.35, '#c8bfae', pat='stone', rows=2)
    for (x, y) in [(X+0.05, Y+1.35), (X+1.75, Y+1.35), (X+1.75, Y+0.05)]:
        c.box(x, y, 0.18, 0.07, 0.07, 1.2, WOOD_L)
    c.line((X+0.05, Y+1.38, 0.9), (X+1.78, Y+1.38, 0.9), WOOD_L, 3); c.line((X+1.78, Y+1.38, 0.9), (X+1.78, Y+0.05, 0.9), WOOD_L, 3)
    c.line((X+0.05, Y+1.38, 0.3), (X+1.78, Y+1.38, 1.3), WOOD_L, 2.4, 0.9)
    c.log((X+0.2, Y+1.7, 0.1), (X+1.0, Y+1.7, 0.1), 0.08, WOOD)
    c.log((X+0.25, Y+1.85, 0.1), (X+1.05, Y+1.85, 0.1), 0.08, WOOD)


def animal(c, x, y, predator=False):
    col = '#6f7275' if predator else '#a7784c'
    c.shadow(x, y, 0.28, 0.2, op=0.3)
    for dx in (-0.14, 0.14):
        for dy in (-0.06, 0.06):
            c.box(x+dx-0.025, y+dy-0.025, 0, 0.05, 0.05, 0.2, shade(col, -0.3))
    c.box(x-0.22, y-0.1, 0.18, 0.44, 0.2, 0.2, col)
    c.box(x+0.18, y-0.07, 0.3, 0.16, 0.14, 0.16, shade(col, 0.08))
    if predator:
        c.poly([(x+0.2, y-0.06, 0.46), (x+0.24, y-0.06, 0.56), (x+0.28, y-0.06, 0.46)], shade(col, -0.2))
        c.box(x-0.3, y-0.03, 0.3, 0.1, 0.06, 0.06, col)
    else:
        c.line((x+0.24, y-0.03, 0.46), (x+0.2, y-0.1, 0.62), '#5a3d22', 2.0)
        c.line((x+0.24, y+0.03, 0.46), (x+0.2, y+0.1, 0.62), '#5a3d22', 2.0)
        c.blob(x-0.24, y, 0.36, 0.04, '#f2e6d0')

def site_marker(c, x, y, col=TEAL):
    c.shadow(x, y, 0.3, 0.3, op=0.3)
    c.cyl(x, y, 0, 0.22, 0.12, STONE)
    banner(c, x, y, 0.12, 1.4, col=col)


# ---------------------------------------------------------------- HUD icons
def icon_food(c, x, y):
    c.cyl(x, y, 0, 0.42, 0.3, '#b9854a', top='#8a5a32')
    for dx, dy in [(-0.12, 0.05), (0.14, -0.06), (0.02, 0.16), (0.0, -0.05)]:
        c.blob(x+dx, y+dy, 0.42, 0.16, '#e0443a', hi=0.6)
    c.blob(x+0.12, y+0.22, 0.38, 0.14, '#f0b43c', hi=0.5)

def icon_wood(c, x, y):
    c.log((x-0.5, y+0.25, 0.22), (x+0.5, y+0.25, 0.22), 0.22, WOOD)
    c.log((x-0.5, y-0.2, 0.22), (x+0.5, y-0.2, 0.22), 0.22, '#955a32')
    c.log((x-0.45, y+0.02, 0.58), (x+0.45, y+0.02, 0.58), 0.22, '#b06c3e')

def icon_stone(c, x, y):
    rocks(c, x, y, 1.3)

def icon_people(c, x, y):
    villager(c, x-0.25, y+0.25, cloth='#c0503a', s=2.2)
    villager(c, x+0.25, y-0.25, cloth='#3d7fc4', s=2.2)
