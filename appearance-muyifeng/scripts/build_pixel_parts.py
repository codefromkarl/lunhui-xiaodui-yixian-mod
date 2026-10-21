#!/usr/bin/env python3
"""Author native sprite texels as editable palette grids. No image input or resizing."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PALETTE = {
    '.': '#00000000', 'o': '#111724', 'd': '#202534', 'h': '#354055',
    'H': '#556079', 'j': '#4C9197', 'J': '#96D6D0', 'K': '#D5F2E6',
    's': '#AF715A', 't': '#D59973', 'f': '#F0C89D', 'F': '#FFE5B9',
    'w': '#E0E9F0', 'W': '#F6F8F5', 'g': '#AFBFD4', 'G': '#7187A5',
    'b': '#42579B', 'B': '#7786C1', 'n': '#26375F', 'e': '#FFF9E8',
    'p': '#45302E', 'a': '#9B8069'
}


class Grid:
    def __init__(self, width, height):
        self.width, self.height = width, height
        self.rows = [['.'] * width for _ in range(height)]

    def pixel(self, x, y, color):
        if 0 <= x < self.width and 0 <= y < self.height:
            self.rows[y][x] = color
        return self

    def box(self, x, y, width, height, color):
        for py in range(y, y + height):
            for px in range(x, x + width):
                self.pixel(px, py, color)
        return self

    def poly(self, points, color):
        # Scan conversion is performed directly in the final logical texel grid.
        for y in range(self.height):
            intersections = []
            for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1]):
                if (y0 <= y + .5 < y1) or (y1 <= y + .5 < y0):
                    intersections.append(x0 + (y + .5 - y0) * (x1 - x0) / (y1 - y0))
            intersections.sort()
            for left, right in zip(intersections[::2], intersections[1::2]):
                for x in range(self.width):
                    if left <= x + .5 < right:
                        self.pixel(x, y, color)
        return self

    def mirrored(self):
        result = Grid(self.width, self.height)
        result.rows = [row[::-1] for row in self.rows]
        return result

    def source(self, pivot, mirror=True):
        # SPUM's canonical art faces left; the player battle mount mirrors it.
        # Reflect the discrete grid and pivot together, without resampling.
        return {'pixelsPerUnit': 56, 'pivot': [1 - pivot[0] if mirror else pivot[0], pivot[1]], 'rows': [''.join(row[::-1] if mirror else row) for row in self.rows]}


def head():
    """Compact three-quarter youth face; draw features as intentional texel clusters."""
    c = Grid(32, 40)
    # Short rounded jaw. The old y=35 chin is replaced by a y=30 chin and a small neck.
    face = {
        14: (12, 23), 15: (11, 25), 16: (10, 26), 17: (10, 27),
        18: (10, 27), 19: (10, 27), 20: (10, 27), 21: (10, 27),
        22: (10, 27), 23: (11, 27), 24: (11, 27), 25: (12, 27),
        26: (12, 26), 27: (13, 26), 28: (14, 25), 29: (15, 24), 30: (17, 22),
    }
    for y, (left, right) in face.items():
        c.box(left, y, right - left + 1, 1, 't' if y >= 26 else 'o')
        if right - left > 1:
            c.box(left + 1, y, right - left - 1, 1, 'f')
    c.box(14, 17, 11, 10, 'F')
    c.box(18, 30, 3, 2, 'f')
    # Small rear ear and restrained side shading, not a continuous brown beard/jaw band.
    c.box(10, 22, 3, 4, 'f').pixel(10, 23, 't').pixel(11, 25, 't')
    c.box(12, 21, 1, 4, 'f').pixel(13, 25, 'f').pixel(14, 26, 'f')
    c.pixel(15, 28, 'f').box(17, 29, 5, 1, 'f')
    # Hair frames the cheeks; the bun/jade silhouette and native head pivot are retained.
    c.poly([(6, 12), (10, 8), (19, 6), (25, 8), (28, 12), (29, 19),
            (27, 24), (24, 27), (25, 22), (25, 18), (20, 14), (16, 16),
            (13, 20), (12, 26), (10, 31), (8, 33), (7, 28), (4, 23), (4, 17)], 'd')
    c.poly([(6, 16), (10, 11), (17, 9), (23, 10), (19, 12), (12, 14), (8, 20), (7, 25), (5, 23)], 'h')
    c.poly([(10, 12), (16, 9), (21, 9), (18, 11), (13, 12)], 'H')
    c.poly([(20, 12), (25, 11), (27, 14), (27, 18), (26, 21), (25, 18), (22, 15)], 'h')
    c.poly([(9, 22), (12, 18), (11, 27), (9, 30), (8, 28)], 'h')
    c.poly([(17, 1), (22, 1), (25, 4), (24, 8), (20, 10), (15, 7), (15, 4)], 'o')
    c.poly([(18, 2), (22, 2), (24, 4), (22, 7), (18, 7), (16, 5)], 'h')
    c.box(19, 2, 3, 1, 'H')
    c.poly([(16, 7), (21, 6), (24, 8), (23, 11), (18, 12), (15, 10)], 'j')
    c.poly([(17, 7), (21, 7), (23, 8), (21, 10), (18, 11), (16, 9)], 'J')
    c.box(18, 8, 3, 2, 'd').pixel(17, 8, 'K').pixel(21, 7, 'K')
    c.poly([(22, 10), (27, 11), (28, 13), (24, 12)], 'J')
    # The NEAR eye sits on the broad rear cheek, the FAR eye close to the nose.
    # Reversing these sizes made the previous face look twisted/flat.
    c.box(14, 19, 5, 1, 'p').pixel(19, 20, 'p')
    c.box(14, 21, 6, 1, 'p').pixel(13, 22, 'p')
    c.box(14, 22, 6, 3, 'e').box(17, 22, 3, 3, 'p')
    c.pixel(17, 23, 'a').pixel(14, 24, 'F').pixel(19, 24, 'f')
    c.box(23, 20, 3, 1, 'p')
    c.box(23, 22, 3, 1, 'p').box(24, 23, 2, 2, 'e').box(25, 23, 1, 2, 'p')
    c.pixel(26, 25, 't')
    c.pixel(24, 27, 't')
    return c


def straight_sword():
    """A narrow symmetric jian using the native blade's upright-right axis and grip."""
    c = Grid(48, 48)
    # Full canvas/pivot match the native asset, but the visible blade occupies only
    # its trimmed 18x34 region. Grip (24, 36); tip (36, 10), in top-down rows.
    c.poly([(36, 10), (36, 16), (30, 29), (28, 31), (26, 28), (32, 15)], 'G')
    c.poly([(35, 13), (35, 16), (29, 29), (28, 29), (28, 27), (33, 16)], 'w')
    c.poly([(35, 14), (35, 16), (29, 28), (28, 28)], 'W')
    c.poly([(33, 17), (34, 17), (29, 28), (28, 28)], 'g')
    c.poly([(22, 27), (34, 31), (33, 33), (22, 29)], 'n')
    c.poly([(23, 27), (33, 31), (32, 32), (23, 29)], 'j')
    c.poly([(26, 31), (29, 32), (24, 41), (21, 40)], 'n')
    c.poly([(27, 33), (28, 33), (24, 40), (23, 40)], 'b')
    c.box(24, 35, 2, 1, 'g').box(23, 38, 2, 1, 'g')
    c.poly([(21, 40), (24, 41), (24, 43), (21, 44), (20, 42)], 'j')
    c.pixel(22, 41, 'J')
    return c


def ponytail():
    c = Grid(24, 40)
    c.poly([(16, 2), (23, 4), (22, 10), (19, 15), (20, 21), (15, 26), (13, 33), (6, 39), (8, 31), (7, 27), (1, 33), (4, 24), (3, 18), (8, 13), (10, 7)], 'o')
    c.poly([(16, 4), (21, 5), (20, 10), (16, 16), (18, 21), (13, 26), (11, 33), (8, 35), (10, 28), (7, 23), (5, 26), (6, 19), (10, 14), (12, 9)], 'd')
    c.poly([(15, 7), (19, 6), (17, 12), (12, 18), (11, 23), (8, 21), (9, 17), (13, 12)], 'h')
    c.poly([(15, 19), (16, 23), (12, 28), (11, 32), (10, 31), (10, 27)], 'h')
    c.poly([(15, 9), (17, 8), (14, 13), (11, 16)], 'H')
    return c


def torso():
    c = Grid(28, 28)
    c.poly([(5, 2), (11, 0), (15, 3), (19, 0), (25, 3), (26, 9), (24, 18), (26, 27), (17, 27), (14, 25), (11, 27), (3, 27), (5, 17), (2, 8)], 'o')
    c.poly([(6, 3), (11, 2), (15, 5), (19, 2), (23, 4), (24, 10), (22, 18), (24, 26), (17, 25), (14, 23), (10, 26), (5, 26), (7, 16), (4, 9)], 'w')
    c.poly([(5, 4), (9, 3), (11, 7), (7, 9), (3, 8)], 'd')
    c.poly([(21, 3), (25, 5), (25, 8), (21, 9), (18, 6)], 'd')
    c.box(5, 7, 3, 1, 'a').box(22, 7, 3, 1, 'a')
    c.poly([(11, 1), (15, 4), (18, 1), (20, 3), (15, 15), (10, 8)], 'n')
    c.poly([(12, 3), (15, 7), (18, 3), (17, 8), (15, 12)], 'b')
    c.poly([(9, 4), (12, 9), (15, 13), (12, 18), (7, 16)], 'W')
    c.poly([(19, 4), (22, 5), (19, 11), (13, 19), (10, 18), (16, 10)], 'W')
    c.poly([(7, 10), (8, 16), (11, 19), (7, 19), (5, 14)], 'g')
    c.poly([(21, 12), (22, 18), (20, 20), (18, 18)], 'g')
    c.box(6, 19, 17, 4, 'n').box(7, 20, 15, 2, 'b').box(8, 20, 10, 1, 'B')
    c.box(18, 19, 4, 4, 'j').box(19, 19, 2, 4, 'J').box(18, 20, 4, 2, 'J').box(19, 20, 2, 2, 'n').pixel(19, 19, 'K')
    c.poly([(8, 23), (12, 23), (10, 27), (5, 27)], 'W')
    c.poly([(16, 23), (21, 23), (24, 27), (18, 26)], 'g')
    return c


def sleeve():
    c = Grid(16, 20)
    c.poly([(6, 0), (12, 2), (13, 7), (15, 14), (11, 20), (3, 18), (0, 12), (4, 7)], 'o')
    c.poly([(6, 2), (11, 3), (12, 9), (14, 14), (10, 18), (4, 16), (2, 12), (6, 7)], 'w')
    c.poly([(6, 1), (11, 2), (12, 6), (5, 7), (4, 5)], 'd')
    c.poly([(5, 6), (12, 5), (12, 7), (5, 8)], 'a')
    c.poly([(7, 8), (11, 8), (13, 14), (10, 17), (5, 15)], 'W')
    c.poly([(3, 11), (5, 12), (7, 16), (5, 17), (2, 13)], 'g')
    c.poly([(3, 16), (10, 18), (14, 14), (14, 17), (11, 20), (3, 18)], 'n')
    c.poly([(4, 16), (10, 18), (13, 16), (11, 19), (4, 17)], 'b')
    return c


def leg():
    c = Grid(12, 23)
    c.poly([(3, 0), (10, 1), (11, 8), (12, 16), (9, 23), (2, 22), (0, 16), (2, 8)], 'o')
    c.poly([(4, 1), (9, 2), (9, 9), (11, 16), (8, 22), (3, 21), (2, 16), (4, 8)], 'w')
    c.poly([(5, 3), (8, 3), (8, 11), (10, 16), (7, 20), (4, 19)], 'W')
    c.poly([(3, 8), (5, 7), (4, 15), (5, 21), (2, 21), (1, 16)], 'g')
    c.poly([(9, 3), (10, 5), (11, 16), (9, 22), (7, 22), (9, 15)], 'b')
    c.box(3, 21, 5, 1, 'n')
    return c


def boot():
    c = Grid(10, 14)
    c.poly([(2, 0), (8, 0), (8, 7), (10, 10), (10, 13), (1, 14), (0, 11), (2, 7)], 'o')
    c.box(3, 1, 4, 8, 'd').box(2, 9, 6, 3, 'd').box(7, 10, 2, 2, 'd')
    c.box(3, 1, 4, 1, 'h').box(3, 3, 1, 4, 'h').box(5, 10, 3, 1, 'H')
    c.box(1, 12, 8, 1, 'h')
    return c


def main():
    arm = sleeve()
    lower = leg()
    sword = straight_sword().source([.5, .25], mirror=False)
    sword['pixelsPerUnit'] = 32
    parts = {
        'Body/Head': head().source([.5625, .425]),
        'HairBack': ponytail().source([.75, .70]),
        'Weapon/Sword': sword,
        'Cloth/Body': torso().source([.5, .58]),
        'Cloth/Left': arm.source([.39, .72]),
        'Cloth/Right': arm.mirrored().source([.61, .72]),
        'Pant/Left': lower.source([.5, .70]),
        'Pant/Right': lower.mirrored().source([.5, .70]),
        'Body/Foot_L': boot().source([.5, .5]),
        'Body/Foot_R': boot().source([.5, .5]),
    }
    result = {'format': 'palette-grid-v1', 'facing': 'body faces left, matching canonical SPUM art; weapon uses its native upright-right diagonal and grip; native player mount mirrors both', 'method': 'authored directly at final texel resolution; no raster input, resize, crop, interpolation or automatic tracing', 'palette': PALETTE, 'parts': parts}
    target = ROOT / 'mod/appearance.muyifeng/art/muyifeng-pixels.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('Authored', len(parts), 'native pixel parts:', target)


if __name__ == '__main__':
    main()
