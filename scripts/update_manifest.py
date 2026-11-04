#!/usr/bin/env python3
"""Ajoute des versions au manifest.json du dépôt de plugins Jellyfin.

update_manifest.py <manifest.json> <owner/repo> <notes.md> "version|targetAbi|url|zip" [...]
"""
import datetime
import hashlib
import json
import pathlib
import sys

GUID = "28e96038-2eff-428b-bdb2-c2115837e144"


def main():
    manifest_path, repo, notes_path, *entries = sys.argv[1:]
    path = pathlib.Path(manifest_path)
    data = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []
    plugin = next((p for p in data if p.get("guid") == GUID), None)
    if plugin is None:
        plugin = {"guid": GUID, "versions": []}
        data.append(plugin)

    owner = repo.split("/")[0]
    plugin.update({
        "name": "Profile Picker",
        "overview": "Écran « Qui regarde ? » avec avatars prédéfinis",
        "description": "Remplace la page de connexion de jellyfin-web par une sélection de profils "
                       "façon plateformes de streaming (avatars, PIN, thèmes) et ajoute une bibliothèque "
                       "d'avatars prédéfinis gérable depuis le tableau de bord.",
        "owner": owner,
        "category": "General",
        "imageUrl": f"https://raw.githubusercontent.com/{repo}/main/docs/logo.png",
    })

    notes = pathlib.Path(notes_path).read_text(encoding="utf-8").strip() if pathlib.Path(notes_path).exists() else ""
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    for entry in entries:
        version, abi, url, zip_path = entry.split("|")
        md5 = hashlib.md5(pathlib.Path(zip_path).read_bytes()).hexdigest()
        plugin["versions"] = [v for v in plugin["versions"] if v.get("version") != version]
        plugin["versions"].append({
            "version": version,
            "changelog": notes,
            "targetAbi": abi,
            "sourceUrl": url,
            "checksum": md5,
            "timestamp": now,
        })

    plugin["versions"].sort(key=lambda v: [int(x) for x in v["version"].split(".")], reverse=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(entries)} version(s) ajoutée(s) — {len(plugin['versions'])} au total")


if __name__ == "__main__":
    main()
