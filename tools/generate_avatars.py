#!/usr/bin/env python3
"""Génère la bibliothèque d'avatars intégrée au plugin (PNG 320x320).

Usage : python3 tools/generate_avatars.py [dossier_sortie]
Par défaut : src/Jellyfin.Plugin.ProfilePicker/Avatars/<categorie>/<nom>.png

Tous les visuels sont des créations géométriques originales (aucun personnage
sous licence), libres de réutilisation avec le plugin.
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

OUT = 320
SS = 4                      # supersampling
S = OUT * SS                # taille de travail
ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
    os.path.dirname(__file__), "..", "src", "Jellyfin.Plugin.ProfilePicker", "Avatars")


def u(v):
    """Coordonnée relative (0..1) -> pixels de travail."""
    return int(v * S)


def box(cx, cy, rx, ry=None):
    ry = rx if ry is None else ry
    return [u(cx - rx), u(cy - ry), u(cx + rx), u(cy + ry)]


def hex2rgb(h):
    if isinstance(h, tuple):
        return h
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def shade(c, f):
    c = hex2rgb(c) if isinstance(c, str) else c
    if f >= 1:
        return tuple(min(255, int(x + (255 - x) * (f - 1))) for x in c)
    return tuple(int(x * f) for x in c)


def canvas(top, bottom=None):
    top = hex2rgb(top)
    bottom = shade(top, 0.62) if bottom is None else hex2rgb(bottom)
    img = Image.new("RGB", (S, S))
    d = ImageDraw.Draw(img)
    for y in range(S):
        t = y / (S - 1)
        d.line([(0, y), (S, y)], fill=tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    # léger vignettage lumineux en haut à gauche
    glow = Image.new("L", (S, S), 0)
    ImageDraw.Draw(glow).ellipse(box(0.25, 0.15, 0.55), fill=60)
    glow = glow.filter(ImageFilter.GaussianBlur(S // 8))
    img = Image.composite(Image.new("RGB", (S, S), (255, 255, 255)), img, glow)
    return img, ImageDraw.Draw(img)


def save(img, cat, name):
    path = os.path.join(ROOT, cat)
    os.makedirs(path, exist_ok=True)
    img.resize((OUT, OUT), Image.LANCZOS).save(os.path.join(path, name + ".png"), optimize=True)


# --------------------------------------------------------------------------- #
# Éléments communs
# --------------------------------------------------------------------------- #
def eyes_round(d, y=0.42, dx=0.14, r=0.055, col="#ffffff"):
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * dx, y, r), fill=col)


def eyes_pupil(d, y=0.42, dx=0.13, r=0.07, pr=0.035, look=(0.012, 0.01)):
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * dx, y, r), fill="#ffffff")
        d.ellipse(box(0.5 + s * dx + look[0], y + look[1], pr), fill="#1b1b1f")
        d.ellipse(box(0.5 + s * dx + look[0] - pr * 0.35, y + look[1] - pr * 0.35, pr * 0.3), fill="#ffffff")


def eyes_bar(d, y=0.40, dx=0.14, w=0.05, h=0.11, col="#ffffff"):
    for s in (-1, 1):
        d.rounded_rectangle(box(0.5 + s * dx, y, w / 2, h / 2), radius=u(w / 2), fill=col)


def smile(d, y=0.58, w=0.20, h=0.12, col="#ffffff", width=0.035):
    d.arc([u(0.5 - w), u(y - h), u(0.5 + w), u(y + h)], 20, 160, fill=col, width=u(width))


def grin(d, y=0.6, w=0.17, col="#ffffff"):
    d.chord([u(0.5 - w), u(y - w * 0.7), u(0.5 + w), u(y + w * 0.7)], 0, 180, fill=col)


def cheeks(d, y=0.55, dx=0.24, col=(255, 140, 160)):
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * dx, y, 0.045, 0.028), fill=col)


# --------------------------------------------------------------------------- #
# Classiques : visages minimalistes sur aplat coloré
# --------------------------------------------------------------------------- #
def classics():
    variants = [
        ("rouge", "#e50914", "bar", "smile"),
        ("bleu", "#1f6feb", "round", "smile"),
        ("jaune", "#f5b301", "bar", "grin"),
        ("vert", "#1aa260", "round", "grin"),
        ("violet", "#7b3fe4", "bar", "smile"),
        ("rose", "#e83e8c", "round", "smile"),
        ("orange", "#f76707", "wink", "smile"),
        ("turquoise", "#0fb5ae", "bar", "o"),
        ("indigo", "#3b48cc", "happy", "smile"),
        ("gris", "#5f6b7a", "round", "flat"),
        ("corail", "#ff6b5b", "happy", "grin"),
        ("menthe", "#2fbf8f", "wink", "grin"),
    ]
    for name, col, eyes, mouth in variants:
        img, d = canvas(col)
        if eyes == "bar":
            eyes_bar(d)
        elif eyes == "round":
            eyes_round(d, r=0.06)
        elif eyes == "wink":
            d.rounded_rectangle(box(0.36, 0.40, 0.025, 0.055), radius=u(0.025), fill="#fff")
            d.arc([u(0.58), u(0.36), u(0.70), u(0.46)], 200, 340, fill="#fff", width=u(0.03))
        elif eyes == "happy":
            for s in (-1, 1):
                d.arc([u(0.5 + s * 0.14 - 0.06), u(0.38), u(0.5 + s * 0.14 + 0.06), u(0.48)], 200, 340, fill="#fff", width=u(0.03))
        if mouth == "smile":
            smile(d)
        elif mouth == "grin":
            grin(d)
        elif mouth == "o":
            d.ellipse(box(0.5, 0.63, 0.05, 0.06), fill="#fff")
        elif mouth == "flat":
            d.rounded_rectangle(box(0.5, 0.62, 0.11, 0.018), radius=u(0.018), fill="#fff")
        save(img, "classiques", name)


# --------------------------------------------------------------------------- #
# Animaux
# --------------------------------------------------------------------------- #
def nose(d, y=0.55, r=0.035, col="#2b2024"):
    d.polygon([(u(0.5 - r), u(y - r * 0.6)), (u(0.5 + r), u(y - r * 0.6)), (u(0.5), u(y + r * 0.7))], fill=col)


def animals():
    # Chat
    img, d = canvas("#8a5cf6")
    fur = "#ffb84d"
    for s in (-1, 1):
        d.polygon([(u(0.5 + s * 0.30), u(0.18)), (u(0.5 + s * 0.12), u(0.38)), (u(0.5 + s * 0.33), u(0.45))], fill=fur)
        d.polygon([(u(0.5 + s * 0.285), u(0.25)), (u(0.5 + s * 0.17), u(0.38)), (u(0.5 + s * 0.30), u(0.42))], fill="#ff8fa3")
    d.ellipse(box(0.5, 0.56, 0.31, 0.27), fill=fur)
    eyes_pupil(d, y=0.52, dx=0.12, r=0.06, pr=0.032)
    nose(d, y=0.62, r=0.028, col="#e2557a")
    for s in (-1, 1):
        for k in (-1, 0, 1):
            d.line([(u(0.5 + s * 0.12), u(0.65 + k * 0.02)), (u(0.5 + s * 0.30), u(0.63 + k * 0.045))], fill="#7a4a12", width=u(0.008))
    save(img, "animaux", "chat")

    # Renard
    img, d = canvas("#0f7b6c")
    o = "#ff7a1a"
    for s in (-1, 1):
        d.polygon([(u(0.5 + s * 0.30), u(0.15)), (u(0.5 + s * 0.10), u(0.40)), (u(0.5 + s * 0.36), u(0.46))], fill=o)
        d.polygon([(u(0.5 + s * 0.28), u(0.24)), (u(0.5 + s * 0.17), u(0.40)), (u(0.5 + s * 0.31), u(0.43))], fill="#3a1a07")
    d.polygon([(u(0.16), u(0.42)), (u(0.84), u(0.42)), (u(0.5), u(0.86))], fill=o)
    d.ellipse(box(0.5, 0.45, 0.34, 0.16), fill=o)
    d.polygon([(u(0.22), u(0.52)), (u(0.5), u(0.58)), (u(0.5), u(0.86))], fill="#fff4e6")
    d.polygon([(u(0.78), u(0.52)), (u(0.5), u(0.58)), (u(0.5), u(0.86))], fill="#fff4e6")
    eyes_round(d, y=0.48, dx=0.13, r=0.035, col="#2a1608")
    d.ellipse(box(0.5, 0.80, 0.04, 0.03), fill="#2a1608")
    save(img, "animaux", "renard")

    # Ours
    img, d = canvas("#3a86ff")
    b = "#8b5a3c"
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.24, 0.27, 0.10), fill=b)
        d.ellipse(box(0.5 + s * 0.24, 0.27, 0.055), fill="#c48a63")
    d.ellipse(box(0.5, 0.55, 0.32, 0.30), fill=b)
    d.ellipse(box(0.5, 0.65, 0.14, 0.11), fill="#d9a77e")
    eyes_round(d, y=0.49, dx=0.12, r=0.035, col="#1d120c")
    d.ellipse(box(0.5, 0.61, 0.045, 0.032), fill="#1d120c")
    d.arc([u(0.44), u(0.62), u(0.56), u(0.71)], 30, 150, fill="#1d120c", width=u(0.012))
    save(img, "animaux", "ours")

    # Panda
    img, d = canvas("#2fbf71")
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.25, 0.27, 0.10), fill="#1c1c22")
    d.ellipse(box(0.5, 0.55, 0.32, 0.30), fill="#fafafa")
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.13, 0.50, 0.075, 0.095), fill="#1c1c22")
        d.ellipse(box(0.5 + s * 0.125, 0.49, 0.028), fill="#ffffff")
    d.ellipse(box(0.5, 0.62, 0.04, 0.028), fill="#1c1c22")
    cheeks(d, y=0.64, dx=0.22, col=(255, 170, 185))
    save(img, "animaux", "panda")

    # Lapin
    img, d = canvas("#ff8fab")
    w = "#fdf7f2"
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.12, 0.22, 0.07, 0.20), fill=w)
        d.ellipse(box(0.5 + s * 0.12, 0.23, 0.035, 0.15), fill="#ffc2d1")
    d.ellipse(box(0.5, 0.60, 0.29, 0.26), fill=w)
    eyes_round(d, y=0.56, dx=0.11, r=0.033, col="#2b2024")
    d.ellipse(box(0.5, 0.65, 0.03, 0.022), fill="#ff6f91")
    d.rounded_rectangle(box(0.5, 0.73, 0.04, 0.03), radius=u(0.01), fill="#ffffff", outline="#e0d6d0", width=u(0.006))
    cheeks(d, y=0.66, dx=0.18)
    save(img, "animaux", "lapin")

    # Hibou
    img, d = canvas("#22223b")
    body = "#9a6b4b"
    for s in (-1, 1):
        d.polygon([(u(0.5 + s * 0.30), u(0.18)), (u(0.5 + s * 0.16), u(0.33)), (u(0.5 + s * 0.32), u(0.36))], fill=body)
    d.ellipse(box(0.5, 0.55, 0.31, 0.32), fill=body)
    d.ellipse(box(0.5, 0.72, 0.18, 0.15), fill="#d9b08c")
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.13, 0.46, 0.11), fill="#fff3d6")
        d.ellipse(box(0.5 + s * 0.13, 0.46, 0.06), fill="#ffb703")
        d.ellipse(box(0.5 + s * 0.13, 0.46, 0.032), fill="#1b1b1f")
    d.polygon([(u(0.47), u(0.55)), (u(0.53), u(0.55)), (u(0.5), u(0.62))], fill="#ffb703")
    save(img, "animaux", "hibou")

    # Pingouin
    img, d = canvas("#48cae4")
    d.ellipse(box(0.5, 0.56, 0.31, 0.33), fill="#1d2433")
    d.ellipse(box(0.5, 0.60, 0.22, 0.26), fill="#ffffff")
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.09, 0.46, 0.035), fill="#1d2433")
        d.ellipse(box(0.5 + s * 0.09 + 0.01, 0.45, 0.01), fill="#ffffff")
    d.polygon([(u(0.44), u(0.53)), (u(0.56), u(0.53)), (u(0.5), u(0.60))], fill="#ff9f1c")
    cheeks(d, y=0.56, dx=0.16)
    save(img, "animaux", "pingouin")

    # Grenouille
    img, d = canvas("#ffd166")
    g = "#38b000"
    d.ellipse(box(0.5, 0.60, 0.34, 0.25), fill=g)
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.17, 0.38, 0.10), fill=g)
        d.ellipse(box(0.5 + s * 0.17, 0.38, 0.065), fill="#ffffff")
        d.ellipse(box(0.5 + s * 0.17, 0.39, 0.035), fill="#1b1b1f")
    d.arc([u(0.30), u(0.50), u(0.70), u(0.70)], 20, 160, fill="#1f6e00", width=u(0.02))
    cheeks(d, y=0.62, dx=0.24)
    save(img, "animaux", "grenouille")

    # Chien
    img, d = canvas("#ef476f")
    fur = "#e9c46a"
    d.ellipse(box(0.5, 0.55, 0.28, 0.29), fill=fur)
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.28, 0.48, 0.09, 0.19), fill="#8d5524")
    d.ellipse(box(0.5, 0.67, 0.15, 0.11), fill="#fff1d0")
    eyes_round(d, y=0.49, dx=0.11, r=0.035, col="#1d120c")
    d.ellipse(box(0.5, 0.62, 0.05, 0.035), fill="#1d120c")
    d.ellipse(box(0.5, 0.75, 0.035, 0.045), fill="#ff6f91")
    save(img, "animaux", "chien")


# --------------------------------------------------------------------------- #
# Robots
# --------------------------------------------------------------------------- #
def robots():
    specs = [
        ("bleu", "#1d3557", "#a8dadc", "round", "#00e5ff"),
        ("orange", "#3d1f0a", "#ffb563", "square", "#ff3d00"),
        ("vert", "#081c15", "#74c69d", "visor", "#b7ff00"),
        ("rose", "#3a0ca3", "#f72585", "round", "#ffffff"),
        ("acier", "#212529", "#adb5bd", "visor", "#ff1744"),
        ("jaune", "#14213d", "#fca311", "square", "#14213d"),
    ]
    for name, bg, metal, kind, led in specs:
        img, d = canvas(bg, shade(bg, 1.35))
        d.line([(u(0.5), u(0.12)), (u(0.5), u(0.25))], fill=shade(metal, 0.7), width=u(0.02))
        d.ellipse(box(0.5, 0.11, 0.035), fill=led)
        for s in (-1, 1):
            d.rounded_rectangle(box(0.5 + s * 0.33, 0.52, 0.04, 0.09), radius=u(0.02), fill=shade(metal, 0.7))
        d.rounded_rectangle(box(0.5, 0.52, 0.30, 0.27), radius=u(0.08), fill=metal)
        d.rounded_rectangle(box(0.5, 0.52, 0.30, 0.27), radius=u(0.08), outline=shade(metal, 0.75), width=u(0.012))
        if kind == "round":
            for s in (-1, 1):
                d.ellipse(box(0.5 + s * 0.12, 0.47, 0.075), fill="#11151c")
                d.ellipse(box(0.5 + s * 0.12, 0.47, 0.04), fill=led)
        elif kind == "square":
            for s in (-1, 1):
                d.rounded_rectangle(box(0.5 + s * 0.12, 0.47, 0.07), radius=u(0.015), fill="#11151c")
                d.rounded_rectangle(box(0.5 + s * 0.12, 0.47, 0.035), radius=u(0.008), fill=led)
        else:
            d.rounded_rectangle(box(0.5, 0.47, 0.23, 0.07), radius=u(0.07), fill="#11151c")
            d.rounded_rectangle(box(0.5, 0.47, 0.17, 0.025), radius=u(0.025), fill=led)
        # grille bouche
        d.rounded_rectangle(box(0.5, 0.65, 0.14, 0.04), radius=u(0.015), fill="#11151c")
        for k in range(-2, 3):
            d.line([(u(0.5 + k * 0.05), u(0.62)), (u(0.5 + k * 0.05), u(0.68))], fill=shade(metal, 0.8), width=u(0.008))
        save(img, "robots", name)


# --------------------------------------------------------------------------- #
# Espace & monstres
# --------------------------------------------------------------------------- #
def space():
    # Alien
    img, d = canvas("#10002b", "#3c096c")
    for x, y, r in [(0.15, 0.2, 0.006), (0.82, 0.15, 0.008), (0.88, 0.7, 0.005), (0.1, 0.8, 0.007), (0.7, 0.88, 0.005)]:
        d.ellipse(box(x, y, r), fill="#ffffff")
    gr = "#80ed99"
    for s in (-1, 1):
        d.line([(u(0.5 + s * 0.1), u(0.28)), (u(0.5 + s * 0.2), u(0.12))], fill=gr, width=u(0.02))
        d.ellipse(box(0.5 + s * 0.2, 0.12, 0.035), fill="#ff4d6d")
    d.ellipse(box(0.5, 0.54, 0.29, 0.30), fill=gr)
    for s in (-1, 1):
        d.ellipse(box(0.5 + s * 0.12, 0.50, 0.09, 0.06), fill="#10002b")
        d.ellipse(box(0.5 + s * 0.12 - 0.03, 0.48, 0.018), fill="#ffffff")
    d.arc([u(0.42), u(0.60), u(0.58), u(0.72)], 20, 160, fill="#10002b", width=u(0.018))
    save(img, "espace", "alien")

    # Astronaute
    img, d = canvas("#03045e", "#0077b6")
    for x, y, r in [(0.12, 0.15, 0.007), (0.85, 0.25, 0.006), (0.2, 0.85, 0.006), (0.9, 0.82, 0.008)]:
        d.ellipse(box(x, y, r), fill="#ffffff")
    d.ellipse(box(0.5, 0.52, 0.33), fill="#f1f3f5")
    d.ellipse(box(0.5, 0.52, 0.33), outline="#ced4da", width=u(0.015))
    d.rounded_rectangle(box(0.5, 0.52, 0.24, 0.17), radius=u(0.13), fill="#1b263b")
    d.rounded_rectangle([u(0.33), u(0.39), u(0.47), u(0.46)], radius=u(0.03), fill="#5390d9")
    d.rounded_rectangle(box(0.5, 0.87, 0.06, 0.03), radius=u(0.02), fill="#ff595e")
    save(img, "espace", "astronaute")

    # Fantôme
    img, d = canvas("#5a189a", "#240046")
    g = "#f8f9fa"
    d.ellipse(box(0.5, 0.42, 0.26, 0.24), fill=g)
    d.rectangle([u(0.24), u(0.42), u(0.76), u(0.78)], fill=g)
    for k in range(4):
        cx = 0.24 + 0.065 + k * 0.13
        d.ellipse(box(cx, 0.78, 0.065, 0.06), fill=g)
    eyes_round(d, y=0.44, dx=0.09, r=0.04, col="#240046")
    d.ellipse(box(0.5, 0.56, 0.04, 0.05), fill="#240046")
    cheeks(d, y=0.52, dx=0.17)
    save(img, "espace", "fantome")

    # Monstre poilu
    img, d = canvas("#ffbe0b", "#fb5607")
    m = "#3a0ca3"
    for k in range(16):
        a = math.pi * 2 * k / 16
        d.ellipse(box(0.5 + math.cos(a) * 0.28, 0.54 + math.sin(a) * 0.28, 0.07), fill=m)
    d.ellipse(box(0.5, 0.54, 0.30), fill=m)
    d.ellipse(box(0.5, 0.44, 0.11), fill="#ffffff")
    d.ellipse(box(0.52, 0.45, 0.05), fill="#1b1b1f")
    d.chord([u(0.32), u(0.52), u(0.68), u(0.78)], 0, 180, fill="#1b1b1f")
    for s in (-1, 1):
        d.polygon([(u(0.5 + s * 0.12), u(0.65)), (u(0.5 + s * 0.06), u(0.65)), (u(0.5 + s * 0.09), u(0.71))], fill="#ffffff")
    save(img, "espace", "monstre")

    # Planète
    img, d = canvas("#0b090a", "#3d0066")
    for x, y, r in [(0.15, 0.2, 0.007), (0.8, 0.12, 0.006), (0.85, 0.8, 0.008), (0.12, 0.75, 0.005)]:
        d.ellipse(box(x, y, r), fill="#ffffff")
    d.ellipse(box(0.5, 0.5, 0.42, 0.12), outline="#ffd6a5", width=u(0.03))
    d.ellipse(box(0.5, 0.5, 0.24), fill="#ff7b00")
    d.chord(box(0.5, 0.5, 0.24), 180, 360, fill="#ff9e00")
    eyes_bar(d, y=0.48, dx=0.08, w=0.035, h=0.07, col="#3d0066")
    smile(d, y=0.53, w=0.07, h=0.05, col="#3d0066", width=0.018)
    d.arc(box(0.5, 0.5, 0.42, 0.12), 0, 180, fill="#ffd6a5", width=u(0.03))
    save(img, "espace", "planete")

    # Fusée
    img, d = canvas("#1a1a2e", "#16213e")
    d.polygon([(u(0.5), u(0.12)), (u(0.62), u(0.32)), (u(0.38), u(0.32))], fill="#e63946")
    d.rounded_rectangle([u(0.38), u(0.30), u(0.62), u(0.72)], radius=u(0.04), fill="#f1faee")
    d.ellipse(box(0.5, 0.45, 0.07), fill="#457b9d")
    d.ellipse(box(0.5, 0.45, 0.07), outline="#a8dadc", width=u(0.012))
    for s in (-1, 1):
        d.polygon([(u(0.5 + s * 0.12), u(0.55)), (u(0.5 + s * 0.22), u(0.76)), (u(0.5 + s * 0.12), u(0.72))], fill="#e63946")
    d.polygon([(u(0.42), u(0.72)), (u(0.58), u(0.72)), (u(0.5), u(0.92))], fill="#ffb703")
    d.polygon([(u(0.46), u(0.72)), (u(0.54), u(0.72)), (u(0.5), u(0.84))], fill="#fb8500")
    save(img, "espace", "fusee")


if __name__ == "__main__":
    classics()
    animals()
    robots()
    space()
    n = sum(len(f) for _, _, f in os.walk(ROOT))
    print(f"{n} avatars générés dans {os.path.abspath(ROOT)}")
