"""Install the public mount item icons locally, alongside the other DOFUS artwork.

PNG files stay under the existing ignored img/dofus directory. This script does
not change the release guard or authorize redistribution of Ankama artwork.
Python 3, standard library only. Run from any working directory.
"""
import argparse
import concurrent.futures
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import struct
import urllib.parse
import urllib.request

CATALOG = "https://api.dofusdu.de/dofus3/v1/fr/mounts/all"
PNG = b"\x89PNG\r\n\x1a\n"


def read_url(url, limit):
    request = urllib.request.Request(url, headers={"User-Agent": "BestCrush-local-artwork/1"})
    with urllib.request.urlopen(request, timeout=45) as response:
        data = response.read(limit + 1)
        if len(data) > limit:
            raise ValueError("Image or catalogue exceeds expected size")
        return data


def validate_png(data):
    if len(data) < 33 or not data.startswith(PNG) or data[12:16] != b"IHDR":
        raise ValueError("Invalid PNG")
    width, height = struct.unpack(">II", data[16:24])
    if not (1 <= width <= 512 and 1 <= height <= 512):
        raise ValueError("Unexpected PNG dimensions")


def install(repo, output, catalogue_bytes=None):
    catalogue = json.loads(catalogue_bytes or read_url(CATALOG, 8_000_000))
    rows = catalogue.get("mounts", [])
    by_item = {row["ankama_id"]: row for row in rows}
    if not rows or len(by_item) != len(rows):
        raise ValueError("Empty catalogue or ambiguous item identifiers")
    rides = json.loads((repo / "BestCrush/Resources/breeding-lineage.json").read_text(encoding="utf-8"))["rides"]
    missing = [ride["itemId"] for ride in rides if ride["itemId"] not in by_item]
    if missing:
        raise ValueError(f"Missing mount item IDs: {missing}")
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=True)

    def download(row):
        item = row["ankama_id"]
        if not isinstance(item, int) or item <= 0:
            raise ValueError("Invalid mount item ID")
        url = row["image_urls"]["sd"]
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme != "https" or parsed.netloc != "api.dofusdu.de" or not parsed.path.startswith("/dofus3/v1/img/item/"):
            raise ValueError("Unexpected artwork origin")
        data = read_url(url, 1_000_000)
        validate_png(data)
        target = output / f"{item}.png"
        temporary = output / f"{item}.png.part"
        temporary.write_bytes(data)
        temporary.replace(target)
        return {"itemId": item, "name": row["name"], "file": target.name, "url": url,
                "sha256": hashlib.sha256(data).hexdigest()}

    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as executor:
        records = sorted(executor.map(download, rows), key=lambda row: row["itemId"])
    manifest = {"source": CATALOG, "rights": "Ankama", "observedAtUtc": datetime.now(timezone.utc).isoformat(),
                "breedingSpecies": len(rides), "coveredBreedingSpecies": len(rides), "images": records}
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Installed {len(records)} mount icons; {len(rides)}/{len(rides)} breeding species covered.")
    print(output)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--catalogue", type=Path, help="Previously downloaded public catalogue (optional)")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    install(root, args.output or root / "BestCrush/wwwroot/img/dofus/mounts",
            args.catalogue.read_bytes() if args.catalogue else None)
