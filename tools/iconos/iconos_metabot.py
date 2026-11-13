"""Arma data/iconos-metabot.json: qué íconos de skills y emblemas de clase baja el meter de MetaBot.GG.

El archivo lleva solo IDs y nombres de clase (las imágenes no van al repo: son arte de NCSOFT). El meter las baja al
abrirse, en la PC de cada usuario, de https://metabot.gg/web/aion2/... (Icons/IconDownloader.cs).

Uso:
    python -I tools/iconos/iconos_metabot.py <carpeta con <id>.png> [--sin-verificar]

La carpeta es una con los íconos ya bajados de https://metabot.gg/en/aion-2/skills (la página dice 280 skills de clase; la carpeta usada tenía 279 íconos).
Cada ID se comprueba con un HEAD a MetaBot (uno cada medio segundo) antes de escribirlo: no entra ninguno que el sitio
no tenga.
"""

import json
import os
import sys
import time
import urllib.error
import urllib.request

SKILL_URL = "https://metabot.gg/web/aion2/skills/{}.webp"
CLASS_URL = "https://metabot.gg/web/aion2/classes/{}.webp"
AGENT = "HysteriaMeter (manifiesto de íconos; atribución a metabot.gg)"

# ID de clase del juego → nombre en las URLs de MetaBot (https://metabot.gg/en/aion-2/classes).
CLASSES = {
    11: "gladiator",
    12: "templar",
    13: "assassin",
    14: "ranger",
    15: "sorcerer",
    16: "spiritmaster",
    17: "cleric",
    18: "chanter",
}


def exists(url):
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": AGENT})
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status == 200 and response.headers.get("Content-Type", "").startswith("image/")
    except urllib.error.HTTPError:
        return False


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    folder = sys.argv[1]
    verify = "--sin-verificar" not in sys.argv
    ids = sorted(int(name[:-4]) for name in os.listdir(folder) if name.endswith(".png") and name[:-4].isdigit())
    if verify:
        missing = []
        for url in [SKILL_URL.format(i) for i in ids] + [CLASS_URL.format(c) for c in CLASSES.values()]:
            if not exists(url):
                missing.append(url)
            time.sleep(0.5)
        if missing:
            print("MetaBot no tiene:", *missing, sep="\n  ")
            return 1

    lines = [
        "{",
        "  // Íconos de skills y emblemas de clase que el meter baja de MetaBot.GG al abrirse (Icons/IconDownloader.cs).",
        "  // Fuente: MetaBot.GG, https://metabot.gg/en/aion-2/skills y https://metabot.gg/en/aion-2/classes, con atribución",
        "  // según sus términos (https://metabot.gg/en/legal/terms-of-service). Las imágenes son de NCSOFT: no van al repo",
        "  // ni al zip, se bajan en la PC de cada usuario a Juego\\iconos y Juego\\clases.",
        "  // Generado con tools/iconos/iconos_metabot.py; cada ID se comprobó contra el sitio.",
        '  "skills": [',
    ]
    for start in range(0, len(ids), 10):
        chunk = ", ".join(str(i) for i in ids[start:start + 10])
        lines.append(f"    {chunk}{',' if start + 10 < len(ids) else ''}")
    lines.append("  ],")
    lines.append('  "classes": {')
    lines.append(",\n".join(f'    "{k}": "{v}"' for k, v in CLASSES.items()))
    lines.append("  }")
    lines.append("}")
    out = os.path.join(os.path.dirname(__file__), "..", "..", "data", "iconos-metabot.json")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"{len(ids)} skills y {len(CLASSES)} clases en {os.path.normpath(out)}")
    json.loads("".join(l for l in lines if not l.strip().startswith("//")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
