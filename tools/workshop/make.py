"""Builds the Steam Workshop upload folder (dist/workshop/) for both game branches: content, preview, item .vdf files
and Publier.bat. The folder is copied as is to C:\\KikiMAIster-workshop on the Windows PC (SteamCMD needs absolute paths).
Run after ./build.sh beta release and ./build.sh default release. Usage: python3 tools/workshop/make.py "<change note>"
"""
import json, os, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "dist", "workshop")
WIN = "C:\\KikiMAIster-workshop"
APPID = "2868840"
IDS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "published_ids.json")  # filled after the first upload

ITEMS = {
    "beta": ("KikiMAIster",
             "For the game's BETA branch (public-beta). On the default branch, subscribe to KikiMAIster (default branch). "
             "Subscribe to only one of the two: they have the same mod id, so the game loads only the first one, whatever your branch. "
             "If you switch branches, swap the subscription (your stats and settings are kept).",
             "Pour la branche BÊTA du jeu. Sur la branche par défaut, abonne-toi à KikiMAIster (default branch). "
             "Abonne-toi à un seul des deux : ils ont le même identifiant, le jeu ne charge que le premier, quelle que soit ta branche. "
             "Si tu changes de branche, change d'abonnement (tes stats et réglages sont gardés)."),
    "default": ("KikiMAIster (default branch)",
                "For the game's DEFAULT branch. On the beta branch, subscribe to KikiMAIster (the beta item). "
                "Subscribe to only one of the two: they have the same mod id, so the game loads only the first one, whatever your branch. "
                "If you switch branches, swap the subscription (your stats and settings are kept).",
                "Pour la branche PAR DÉFAUT du jeu. Sur la branche bêta, abonne-toi à KikiMAIster (l'objet bêta). "
                "Abonne-toi à un seul des deux : ils ont le même identifiant, le jeu ne charge que le premier, quelle que soit ta branche. "
                "Si tu changes de branche, change d'abonnement (tes stats et réglages sont gardés)."),
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
        src = os.path.join(ROOT, "dist", branch, "KikiMAIster")
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
                f'+workshop_build_item "{WIN}\\default.vdf" +quit\r\n'
                'echo.\r\necho Copie les deux lignes "PublishFileID" ci-dessus et envoie-les a Claude.\r\npause\r\n')
    print("Ready:", OUT)


main()
