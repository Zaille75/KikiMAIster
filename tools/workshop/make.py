"""Builds the Steam Workshop upload folder (dist/workshop/): content, preview, item .vdf files and Publier.bat. The folder
is copied as is to C:\\KikiMAIster-workshop on the Windows PC (SteamCMD needs absolute paths).
Since 1.1.0 one build runs on both game branches: the main item ("beta" id) gets it, and the old "default branch" item
gets the same build with a "retired, subscribe to KikiMAIster" description (whichever the game loads works).
Run after ./build.sh release. Usage: python3 tools/workshop/make.py "<change note>"
"""
import json, os, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "dist", "workshop")
WIN = "C:\\KikiMAIster-workshop"
APPID = "2868840"
IDS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "published_ids.json")  # filled after the first upload
MAIN_URL = "https://steamcommunity.com/sharedfiles/filedetails/?id=3832591950"

# Key (as in published_ids.json): title, English and French lines put in the description.
ITEMS = {
    "beta": ("KikiMAIster",
             "Works on both game branches (default and beta).",
             "Marche sur les deux branches du jeu (par défaut et bêta)."),
    "default": ("KikiMAIster (default branch) - retired",
                f"RETIRED: KikiMAIster now works on both game branches. Unsubscribe from this item and subscribe to "
                f"[url={MAIN_URL}]KikiMAIster[/url] (your stats and settings are kept). Until then this item gets the same build.",
                f"RETIRÉ : KikiMAIster marche maintenant sur les deux branches du jeu. Désabonne-toi de cet objet et abonne-toi à "
                f"[url={MAIN_URL}]KikiMAIster[/url] (tes stats et réglages sont gardés). En attendant, cet objet reçoit la même version."),
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
                f'+workshop_build_item "{WIN}\\default.vdf" +quit\r\n'
                'echo.\r\necho Copie les deux lignes "PublishFileID" ci-dessus et envoie-les a Claude.\r\npause\r\n')
    print("Ready:", OUT)


main()
