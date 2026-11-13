"""Genera data/skills-taengu.json: los nombres de skill de taengu/AION2-DPS-Meter (licencia MIT), con su licencia.

Uso (desde cualquier carpeta):
    python tools/skills/skills_taengu.py <commit de taengu/AION2-DPS-Meter>

Baja src/main/resources/i18n/skills/en.json y LICENSE de ese commit y escribe data/skills-taengu.json. El mismo commit
da siempre el mismo archivo, así una actualización se revisa como un diff. Es la lista que viene en el zip: sin ella,
quien no armó su Juego/skills.json ve "Skill <ID>". Juego/skills.json y data/skills.json pisan a este.
"""
import json
import sys
import urllib.request
from pathlib import Path

REPO = "taengu/AION2-DPS-Meter"
SKILLS = "src/main/resources/i18n/skills/en.json"
SALIDA = Path(__file__).resolve().parents[2] / "data" / "skills-taengu.json"


def bajar(url):
    pedido = urllib.request.Request(url, headers={"User-Agent": "HysteriaMeter-skills"})
    with urllib.request.urlopen(pedido, timeout=60) as respuesta:
        return respuesta.read().decode("utf-8")


def generar(skills, licencia, commit, fecha):
    lineas = [
        "{",
        "  // Nombres de skills de AION 2 por ID (el paquete de daño 0x3804 solo trae el ID). Las skills de clase van por la",
        "  // skill base: los últimos 4 dígitos del ID del paquete son nivel y variante.",
        f"  // Fuente: {REPO}, {SKILLS},",
        f"  // commit {commit} ({fecha}), licencia MIT (el texto va abajo).",
        "  // Generado con tools/skills/skills_taengu.py: no editar a mano. Lo pisan Juego/skills.json y data/skills.json; un",
        "  // nombre que falte o esté mal se corrige en data/skills.json.",
        "  //",
    ]
    lineas += [f"  // {linea}".rstrip() for linea in licencia.strip().splitlines()]
    lineas.append('  "skills": {')
    ids = sorted(skills, key=int)
    for i, skill in enumerate(ids):
        nombre = json.dumps(skills[skill], ensure_ascii=False)
        lineas.append(f'    "{int(skill)}": {nombre}{"," if i < len(ids) - 1 else ""}')
    lineas += ["  }", "}", ""]
    return "\n".join(lineas)


def main():
    if len(sys.argv) != 2 or len(sys.argv[1]) != 40:
        print(__doc__)
        return 2
    commit = sys.argv[1]
    crudo = f"https://raw.githubusercontent.com/{REPO}/{commit}"
    skills = json.loads(bajar(f"{crudo}/{SKILLS}"))
    licencia = bajar(f"{crudo}/LICENSE")
    if "MIT License" not in licencia:
        print("La licencia de ese commit no es MIT: no se genera nada.")
        return 1
    fecha = json.loads(bajar(f"https://api.github.com/repos/{REPO}/commits/{commit}"))["commit"]["committer"]["date"][:10]
    # El meter lee IDs u32: se saltean las claves que no son números y los nombres vacíos.
    validos = {
        k: v.strip() for k, v in skills.items() if k.isdigit() and int(k) < 2**32 and isinstance(v, str) and v.strip()
    }
    SALIDA.write_text(generar(validos, licencia, commit, fecha), encoding="utf-8", newline="\n")
    print(f"{SALIDA}: {len(validos)} skills")
    return 0


if __name__ == "__main__":
    sys.exit(main())
