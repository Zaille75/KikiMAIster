"""Builds the Steam Workshop upload folder (dist/workshop/): content, preview, item .vdf files and Publier.bat. The folder
is copied as is to C:\\KikiMAIster-workshop on the Windows PC (SteamCMD needs absolute paths).
Since 1.1.0 one build runs on both game branches: one item, "KikiMAIster" (the former beta item, "beta" id). The old
"default branch" item (3832592056) was made invisible by Zaille75 on 2026-11-03 (no subscribers) and is no longer updated.
Run after ./build.sh release. Usage: python3 tools/workshop/make.py "<change note>"
"""
import json, os, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "dist", "workshop")
WIN = "C:\\KikiMAIster-workshop"
APPID = "2868840"
IDS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "published_ids.json")  # filled after the first upload

# Key (as in published_ids.json): title, English and French lines put in the description.
ITEMS = {
    "beta": ("KikiMAIster - Solo and Co-op stats",  # renamed on Steam by Zaille75 (2026-11-06)
             "Works on both game branches: beta v0.113.0 (the Fool, Blitz, Dread) and default v0.111.0. "
             "Version 1.2.2 knows the Fool's arrows and melodies (Wah / Cho / Ki: cards played in the wrong order, melody powers "
             "played late, Vers Ki wasted) and more arrow mistakes; tarot cards will follow.",
             "Marche sur les deux branches du jeu : bêta v0.113.0 (l'Insensé, Blitz, Dread) et par défaut v0.111.0. "
             "La version 1.2.2 connaît les flèches et les mélodies de l'Insensé (Wah / Cho / Ki : cartes jouées dans le mauvais ordre, "
             "pouvoirs de mélodie joués trop tard, Vers Ki gâché) et d'autres erreurs de flèches ; les cartes de tarot suivront."),
}


def q(text):
    """A VDF string value: backslashes and quotes escaped."""
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main():
    note = sys.argv[1] if len(sys.argv) > 1 else ""
    ids = json.load(open(IDS)) if os.path.exists(IDS) else {}
    template = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "description.bbcode"), encoding="utf-8").read()
    shutil.rmtree(OUT, ignore_errors=True)
    os.makedirs(OUT)
    shutil.copy(os.path.join(ROOT, "docs", "images", "icon.png"), os.path.join(OUT, "preview.png"))
    for branch, (title, en, fr) in ITEMS.items():
        src = os.path.join(ROOT, "dist", "KikiMAIster")
        manifest = json.load(open(os.path.join(src, "KikiMAIster.json")))
        shutil.copytree(src, os.path.join(OUT, branch, "content", "KikiMAIster"))
        desc = template.replace("{BRANCH_EN}", en).replace("{BRANCH_FR}", fr)
        fields = [("appid", APPID), ("publishedfileid", str(ids.get(branch, 0))),
                  ("contentfolder", f"{WIN}\\{branch}\\content"), ("previewfile", f"{WIN}\\preview.png"),
                  ("visibility", "2" if branch not in ids else None),  # hidden at creation; later uploads keep it
                  ("title", title), ("description", desc), ("changenote", f"{manifest['version']}: {note}".strip(": "))]
        body = "".join(f"\t{q(k)}\t{q(v)}\n" for k, v in fields if v is not None)
        with open(os.path.join(OUT, f"{branch}.vdf"), "w", encoding="utf-8", newline="\r\n") as f:
            f.write('"workshopitem"\n{\n' + body + "}\n")
        print(f"{branch}: {title} v{manifest['version']} (publishedfileid {ids.get(branch, 0)})")
    with open(os.path.join(OUT, "Publier.bat"), "w", encoding="utf-8", newline="\r\n") as f:
        f.write("@echo off\r\nset /p STEAMUSER=Compte Steam : \r\n"
                f'C:\\steamcmd\\steamcmd.exe +login %STEAMUSER% +workshop_build_item "{WIN}\\beta.vdf" '
                f'+quit\r\n'
                'echo.\r\necho Termine. Dis a Claude que c\'est publie : il verifie la page Steam.\r\npause\r\n')
    print("Ready:", OUT)


main()
