"""Genera data/npcs-taengu.json: los nombres de NPC de taengu/AION2-DPS-Meter (licencia MIT), con su licencia.

Uso (desde cualquier carpeta):
    python tools/npcs/npcs_taengu.py <commit de taengu/AION2-DPS-Meter>

Baja src/main/resources/i18n/npcs/en.json y LICENSE de ese commit y escribe data/npcs-taengu.json. El mismo commit da
siempre el mismo archivo, así una actualización se revisa como un diff. El juego no manda el nombre de los monstruos:
la app los busca por el ID de plantilla (el npcId del spawn 0x3641). Juego/npcs.json y data/npcs.json pisan a este.
"""
import json
import sys
import urllib.request
from pathlib import Path

REPO = "taengu/AION2-DPS-Meter"
NPCS = "src/main/resources/i18n/npcs/en.json"
SALIDA = Path(__file__).resolve().parents[2] / "data" / "npcs-taengu.json"


def bajar(url):
    pedido = urllib.request.Request(url, headers={"User-Agent": "HysteriaMeter-npcs"})
    with urllib.request.urlopen(pedido, timeout=60) as respuesta:
        return respuesta.read().decode("utf-8")


def generar(npcs, licencia, commit, fecha):
    lineas = [
        "{",
        "  // Nombres de NPC de AION 2 por ID de plantilla (el npcId del spawn 0x3641): el juego no manda el nombre.",
        f"  // Fuente: {REPO}, {NPCS},",
        f"  // commit {commit} ({fecha}), licencia MIT (el texto va abajo). \"boss\": true sale de su isBoss.",
        "  // Generado con tools/npcs/npcs_taengu.py: no editar a mano. Lo pisan Juego/npcs.json y data/npcs.json; un nombre",
        "  // que falte o esté mal se corrige en data/npcs.json.",
        "  //",
    ]
    lineas += [f"  // {linea}".rstrip() for linea in licencia.strip().splitlines()]
    lineas.append('  "npcs": {')
    ids = sorted(npcs, key=int)
    for i, npc in enumerate(ids):
        datos = npcs[npc]
        nombre = json.dumps(datos["name"].strip(), ensure_ascii=False)
        valor = f'{{ "name": {nombre}, "boss": true }}' if datos.get("isBoss") is True else nombre
        lineas.append(f'    "{int(npc)}": {valor}{"," if i < len(ids) - 1 else ""}')
    lineas += ["  }", "}", ""]
    return "\n".join(lineas)


def main():
    if len(sys.argv) != 2 or len(sys.argv[1]) != 40:
        print(__doc__)
        return 2
    commit = sys.argv[1]
    crudo = f"https://raw.githubusercontent.com/{REPO}/{commit}"
    npcs = json.loads(bajar(f"{crudo}/{NPCS}"))
    licencia = bajar(f"{crudo}/LICENSE")
    if "MIT License" not in licencia:
        print("La licencia de ese commit no es MIT: no se genera nada.")
        return 1
    fecha = json.loads(bajar(f"https://api.github.com/repos/{REPO}/commits/{commit}"))["commit"]["committer"]["date"][:10]
    validos = {k: v for k, v in npcs.items() if k.isdigit() and isinstance(v.get("name"), str) and v["name"].strip()}
    SALIDA.write_text(generar(validos, licencia, commit, fecha), encoding="utf-8", newline="\n")
    print(f"{SALIDA}: {len(validos)} NPCs, {sum(1 for v in validos.values() if v.get('isBoss') is True)} boss")
    return 0


if __name__ == "__main__":
    sys.exit(main())
