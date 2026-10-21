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

    def source(self, pivot):
        # SPUM's canonical art faces left; the player battle mount mirrors it.
        # Reflect the discrete grid and pivot together, without resampling.
        return {'pixelsPerUnit': 56, 'pivot': [1 - pivot[0], pivot[1]], 'rows': [''.join(row[::-1]) for row in self.rows]}


def head():
    c = Grid(32, 40)
    c.poly([(7, 12), (12, 8), (23, 9), (29, 15), (30, 23), (28, 26), (29, 28), (27, 30), (27, 33), (23, 36), (17, 36), (12, 32), (9, 27), (5, 21)], 'o')
    c.poly([(13, 14), (25, 15), (27, 21), (27, 25), (29, 27), (26, 29), (26, 32), (23, 35), (18, 34), (12, 30), (10, 24), (11, 17)], 'f')
    c.poly([(12, 17), (16, 17), (15, 25), (17, 31), (23, 34), (18, 34), (12, 30), (10, 24)], 't')
    c.poly([(17, 17), (24, 17), (26, 21), (26, 25), (25, 29), (21, 32), (17, 30), (15, 25)], 'F')
    c.poly([(17, 32), (23, 34), (26, 31), (25, 34), (23, 35), (19, 35)], 's')
    c.box(10, 25, 3, 5, 't').box(11, 26, 2, 2, 'f').pixel(11, 28, 's')
    # Hair is composed around the face, rather than placed over a vanilla head.
    c.poly([(6, 12), (10, 8), (19, 6), (25, 8), (28, 12), (29, 19), (26, 21), (23, 17), (19, 15), (15, 17), (12, 22), (11, 29), (8, 33), (7, 28), (4, 23), (4, 17)], 'd')
    c.poly([(6, 16), (10, 11), (17, 9), (23, 10), (19, 12), (12, 14), (8, 20), (7, 25), (5, 23)], 'h')
    c.poly([(10, 12), (16, 9), (21, 9), (18, 11), (13, 12)], 'H')
    c.poly([(20, 12), (25, 11), (27, 14), (27, 18), (25, 18), (22, 15)], 'h')
    c.poly([(9, 22), (12, 18), (11, 27), (9, 30), (8, 28)], 'h')
    c.poly([(17, 1), (22, 1), (25, 4), (24, 8), (20, 10), (15, 7), (15, 4)], 'o')
    c.poly([(18, 2), (22, 2), (24, 4), (22, 7), (18, 7), (16, 5)], 'h')
    c.box(19, 2, 3, 1, 'H')
    c.poly([(16, 7), (21, 6), (24, 8), (23, 11), (18, 12), (15, 10)], 'j')
    c.poly([(17, 7), (21, 7), (23, 8), (21, 10), (18, 11), (16, 9)], 'J')
    c.box(18, 8, 3, 2, 'd').pixel(17, 8, 'K').pixel(21, 7, 'K')
    c.poly([(22, 10), (27, 11), (28, 13), (24, 12)], 'J')
    # Explicit eyebrow, sclera, pupil and mouth clusters at native resolution.
    c.box(14, 20, 3, 1, 'o').pixel(14, 21, 'o')
    c.box(21, 20, 4, 1, 'o').box(24, 19, 2, 1, 'o')
    c.box(14, 23, 3, 2, 'e').box(15, 23, 2, 2, 'p').box(14, 22, 3, 1, 'o')
    c.box(21, 23, 5, 2, 'e').box(24, 23, 2, 2, 'p').box(21, 22, 5, 1, 'o').pixel(25, 21, 'o')
    c.pixel(22, 24, 'f').pixel(26, 25, 't').pixel(27, 27, 's')
    c.box(23, 30, 3, 1, 'p').pixel(23, 31, 'F')
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
    parts = {
        'Body/Head': head().source([.5625, .35]),
        'HairBack': ponytail().source([.75, .70]),
        'Cloth/Body': torso().source([.5, .58]),
        'Cloth/Left': arm.source([.39, .72]),
        'Cloth/Right': arm.mirrored().source([.61, .72]),
        'Pant/Left': lower.source([.5, .70]),
        'Pant/Right': lower.mirrored().source([.5, .70]),
        'Body/Foot_L': boot().source([.5, .5]),
        'Body/Foot_R': boot().source([.5, .5]),
    }
    result = {'format': 'palette-grid-v1', 'facing': 'left, matching canonical SPUM art; native player mount mirrors to face right', 'method': 'authored directly at final texel resolution; no raster input, resize, crop, interpolation or automatic tracing', 'palette': PALETTE, 'parts': parts}
    target = ROOT / 'mod/appearance.muyifeng/art/muyifeng-pixels.json'
    target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('Authored', len(parts), 'native pixel parts:', target)


if __name__ == '__main__':
    main()
