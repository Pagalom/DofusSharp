"""Build versioned, server-independent pet-food coefficients from extracted data.

Inputs are JSON data exports, not executable client files. No game interaction,
network access or database writes. See docs/pet-food-yields.md.
"""
import argparse
import json
import math
import struct
from collections import Counter
from functools import lru_cache
from pathlib import Path


def refs(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))["references"]["RefIds"]


def build(items_path, monsters_path, recipes_path, types_path, names_path):
    all_refs = refs(items_path)
    effects = {r["rid"]: r["data"] for r in all_refs}
    items = {r["data"]["id"]: r["data"] for r in all_refs if "typeId" in r["data"]}
    monsters = {r["data"]["id"]: r["data"] for r in refs(monsters_path) if r["type"]["class"] == "MonsterData"}
    recipes = {r["data"]["resultId"]: r["data"] for r in refs(recipes_path)}
    types = {r["data"]["id"]: r["data"] for r in refs(types_path)}
    names = {r["id"]: r["name"]["fr"] for r in json.loads(names_path.read_text(encoding="utf-8-sig"))}

    @lru_cache(maxsize=None)
    def ingredients(item_id, stack=()):
        if item_id in stack:
            raise ValueError(f"Recipe cycle: {stack + (item_id,)}")
        recipe = recipes.get(item_id)
        if recipe is None:
            return ((item_id, 1),)
        result = Counter()
        for ingredient, count in zip(recipe["ingredientIds"], recipe["quantities"], strict=True):
            for leaf, quantity in ingredients(ingredient, stack + (item_id,)):
                result[leaf] += count * quantity
        return tuple(result.items())

    def boss_factor(item_id):
        sources = items[item_id]["dropMonsterIds"]
        # This is the client's exact condition, including unresolved monster IDs.
        exclusively_boss = bool(sources) and all(monsters[m]["m_flags"] & 4 for m in sources if m in monsters)
        return 5.0 if exclusively_boss else 1.0

    def coefficient(item_id):
        item_effects = [effects[e["rid"]] for e in items[item_id]["possibleEffects"]]
        if any(e.get("actionId") in {2862, 2864} for e in item_effects):
            return 0.0
        explicit = sum(e.get("value", 0) for e in item_effects if e.get("actionId") == 2820)
        if explicit > 0:
            return float(explicit)
        crafted = 1.5 if item_id in recipes else 1.0
        total = sum(quantity * items[leaf]["recyclingNuggets"] * (boss_factor(leaf) * crafted)
                    for leaf, quantity in ingredients(item_id))
        scaled = struct.unpack("<f", struct.pack("<f", total * 100000.0))[0]
        return (math.floor(scaled) / 100000.0) * 10.0

    records = []
    for item_id, item in sorted(items.items()):
        if types.get(item["typeId"], {}).get("superTypeId") != 9 and item["typeId"] != 209:
            continue
        xp = coefficient(item_id)
        if not math.isfinite(xp) or xp < 0:
            raise ValueError(f"Invalid XP: {item_id}")
        records.append(dict(Id=item_id, Name=names[item_id], IconId=item["iconId"],
                            MarketType=2 if item["typeId"] == 78 else 1,
                            Experience=xp, Craftable=item_id in recipes))
    return records


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ["items", "monsters", "recipes", "types", "names", "output"]:
        parser.add_argument("--" + name, type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--client-version", required=True)
    args = parser.parse_args()
    rows = build(args.items, args.monsters, args.recipes, args.types, args.names)
    args.output.write_text(json.dumps(dict(Version=args.version, ClientVersion=args.client_version, Records=rows),
                                      ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"{len(rows)} entries, {sum(r['Experience'] > 0 for r in rows)} positive yields")
