#!/usr/bin/env python3
"""Read-only timeline of Dofus network traffic around a manual crush.

This script never writes to the capture, SQLite, or any other file.
Do not share wire.jsonl: it contains unredacted raw network payloads.
"""
from __future__ import annotations

import argparse
import base64
import binascii
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
import json
import math
from pathlib import Path
import struct
import sys

from inspect_jzs_market import fields, latest_capture, read_varint


@dataclass(frozen=True)
class WireRecord:
    utc: datetime
    direction: str
    key: str
    body_length: int
    body_base64: str


def parse_record(line: str) -> WireRecord | None:
    try:
        row = json.loads(line)
        if not isinstance(row, dict):
            return None
        utc = datetime.fromisoformat(row["utc"].replace("Z", "+00:00"))
        if utc.tzinfo is None:
            return None
        key, direction = row["key"], row["direction"]
        length = row["bodyLength"]
        if (not isinstance(key, str) or not 1 <= len(key) <= 16 or
                direction not in ("S→C", "C→S") or
                type(length) is not int or not 0 <= length <= 16_777_216):
            return None
        body = row.get("bodyBase64", "")
        if not isinstance(body, str):
            return None
        return WireRecord(
            utc.astimezone(timezone.utc), direction, key, length, body
        )
    except (ValueError, TypeError, KeyError, AttributeError):
        return None


def read_records(path: Path):
    with path.open("r", encoding="utf-8-sig") as source:
        for line in source:
            parsed = parse_record(line)
            if parsed is not None:
                yield parsed


def last_timestamp(path: Path) -> datetime | None:
    latest = None
    for record in read_records(path):
        if latest is None or record.utc > latest:
            latest = record.utc
    return latest


def summarize(path: Path, latest: datetime, seconds: int, bin_seconds: int,
              direction: str = "both", keys: set[str] | None = None,
              sample_limit: int = 2) -> dict:
    cutoff = latest - timedelta(seconds=seconds)
    totals = Counter()
    byte_ranges: dict[tuple[str, str], list[int]] = {}
    time_ranges: dict[tuple[str, str], list[datetime]] = {}
    timeline = defaultdict(Counter)
    samples: dict[str, list[WireRecord]] = defaultdict(list)
    selected = 0
    for record in read_records(path):
        if record.utc < cutoff or record.utc > latest:
            continue
        if direction != "both" and record.direction != direction:
            continue
        selected += 1
        pair = (record.direction, record.key)
        totals[pair] += 1
        bounds = byte_ranges.setdefault(
            pair, [record.body_length, record.body_length])
        bounds[0] = min(bounds[0], record.body_length)
        bounds[1] = max(bounds[1], record.body_length)
        stamps = time_ranges.setdefault(pair, [record.utc, record.utc])
        stamps[0] = min(stamps[0], record.utc)
        stamps[1] = max(stamps[1], record.utc)
        bucket = min((seconds - 1) // bin_seconds,
                     int((record.utc - cutoff).total_seconds()) // bin_seconds)
        timeline[bucket][pair] += 1
        if keys and record.key in keys:
            # Prefer the most RECENT occurrences of a candidate opcode:
            # the crush can happen late in the selected time window.
            selected_samples = samples[record.key]
            selected_samples.append(record)
            if len(selected_samples) > sample_limit:
                selected_samples.pop(0)

    return {
        "selected": selected,
        "totals": totals,
        "sizes": byte_ranges,
        "times": time_ranges,
        "timeline": timeline,
        "samples": samples,
        "cutoff": cutoff,
        "latest": latest,
        "seconds": seconds,
        "bin_seconds": bin_seconds,
    }


def numeric_tree(body: bytes, max_depth: int = 3, depth: int = 0,
                 budget: list[int] | None = None) -> list[str]:
    """Bounded structural hypotheses only; no hex, strings, or byte dumps."""
    if budget is None:
        budget = [50]
    indent = "  " * depth
    try:
        parsed = fields(body)
    except ValueError:
        return [indent + "(protobuf non reconnu)"]
    output = []
    for field in parsed:
        if budget[0] <= 0:
            output.append(indent + "(limite d'affichage atteinte)")
            break
        budget[0] -= 1
        prefix = f"{indent}f{field.number} wire={field.wire_type}"
        if field.wire_type == 0:
            output.append(f"{prefix} varint={field.value}")
        elif field.wire_type == 5:
            as_float = struct.unpack("<f", struct.pack("<I", field.value))[0]
            if math.isfinite(as_float) and 0 < as_float <= 10:
                output.append(f"{prefix} fixed32 float?={as_float:.7g}")
            else:
                output.append(f"{prefix} fixed32 (valeur non interprétée)")
        elif field.wire_type == 1:
            output.append(f"{prefix} fixed64 (valeur non interprétée)")
        else:
            value = field.value
            if depth < max_depth:
                try:
                    nested = fields(value)
                except ValueError:
                    nested = []
                if nested:
                    output.append(f"{prefix} message?[{len(value)}]")
                    output.extend(numeric_tree(
                        value, max_depth, depth + 1, budget))
                    continue
            packed = []
            pos = 0
            try:
                while pos < len(value) and len(packed) <= 8:
                    v, pos = read_varint(value, pos)
                    packed.append(v)
            except ValueError:
                packed = []
            if packed and pos == len(value) and len(packed) <= 8:
                output.append(f"{prefix} bytes[{len(value)}] "
                              f"varints?={packed}")
            else:
                output.append(f"{prefix} bytes[{len(value)}] (opaque)")
    return output


def show_summary(result: dict, top: int) -> None:
    print(f"Fenêtre : {result['cutoff'].isoformat()} "
          f"→ {result['latest'].isoformat()} UTC")
    print(f"Messages analysés : {result['selected']}; "
          "ni contenu brut ni écriture de données.")
    print()
    if not result["selected"]:
        print("Aucun message valide dans cette fenêtre.")
        return

    print("Codes par fréquence (les clés ne sont PAS identifiées métier) :")
    for (direction, key), count in result["totals"].most_common(top):
        size = result["sizes"][(direction, key)]
        end = result["times"][(direction, key)][1]
        since = (end - result["cutoff"]).total_seconds()
        print(f"  {direction} {key:<12} x{count:<5} "
              f"octets={size[0]}..{size[1]}  "
              f"dernier=+{since:.1f}s")
    if len(result["totals"]) > top:
        print(f"  ... {len(result['totals']) - top} autres couples clé/sens")

    print()
    print("Chronologie par tranches (secondes depuis début de fenêtre) :")
    bin_s = result["bin_seconds"]
    seconds = result["seconds"]
    for bucket, counts in sorted(result["timeline"].items()):
        start = bucket * bin_s
        end = min(start + bin_s, seconds)
        top_keys = counts.most_common(8)
        values = " ".join(
            f"{direc}:{key}×{count}" for (direc, key), count in top_keys)
        remaining = sum(counts.values()) - sum(n for _, n in top_keys)
        if remaining:
            values += f" (+{remaining} autres messages)"
        print(f"  +{start:>3}..+{end:<3}s  {values}")


def show_key_samples(result: dict, keys: list[str], depth: int) -> None:
    for key in keys:
        print()
        samples = result["samples"].get(key, [])
        print(f"Structure candidate du code {key} "
              f"({len(samples)} échantillon(s) affiché(s)) :")
        if not samples:
            print("  Aucun paquet de ce code dans la fenêtre.")
            continue
        for sample in samples:
            print(f"  {sample.utc.isoformat()} {sample.direction} "
                  f"{sample.body_length} octets")
            if sample.body_length > 1_048_576:
                print("    (corps trop long pour un aperçu)")
                continue
            try:
                body = base64.b64decode(
                    sample.body_base64, validate=True)
            except (ValueError, binascii.Error):
                print("    (Base64 invalide)")
                continue
            if len(body) != sample.body_length:
                print("    (longueur de corps incohérente)")
                continue
            for row in numeric_tree(body, max_depth=depth):
                print("    " + row)
    if keys:
        print()
        print("Champs/float? = hypothèses structurelles, PAS des coefficients "
              "ni des runes confirmés.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", type=Path,
                        help="sélectionner explicitement un wire.jsonl")
    parser.add_argument("--last-seconds", type=int, default=60,
                        help="fenêtre précédant le DERNIER paquet (défaut 60s)")
    parser.add_argument("--bin-seconds", type=int, default=5,
                        help="résolution de la chronologie (défaut 5s)")
    parser.add_argument("--top", type=int, default=35,
                        help="nombre de codes dans le résumé (défaut 35)")
    parser.add_argument("--direction", choices=["both", "S→C", "C→S"],
                        default="both", help="sens à afficher")
    parser.add_argument("--key", action="append", default=[],
                        help="code à inspecter (répéter pour plusieurs codes)")
    parser.add_argument("--samples", type=int, default=2,
                        help="échantillons affichés par code (défaut 2)")
    parser.add_argument("--depth", type=int, default=3,
                        help="profondeur des champs possibles (défaut 3)")
    args = parser.parse_args(argv)
    if not (1 <= args.last_seconds <= 3600 and
            1 <= args.bin_seconds <= 300 and
            1 <= args.top <= 100 and
            1 <= args.samples <= 8 and
            0 <= args.depth <= 5):
        parser.error("hors limites : durée 1..3600, tranche 1..300, "
                     "top 1..100, échantillons 1..8, profondeur 0..5")
    if any(not key or len(key) > 16 for key in args.key):
        parser.error("--key doit contenir un code court")
    try:
        path = args.file or latest_capture()
        if not path.is_file():
            raise FileNotFoundError(str(path))
        latest = last_timestamp(path)
        if latest is None:
            print("Aucun enregistrement valide dans ce fichier.")
            return 1
        result = summarize(
            path, latest, args.last_seconds, args.bin_seconds,
            direction=args.direction, keys=set(args.key),
            sample_limit=args.samples)
    except (FileNotFoundError, OSError) as exc:
        print(f"Capture inaccessible : {exc}", file=sys.stderr)
        return 1

    print(f"Diagnostic P3.0 en LECTURE SEULE : {path.parent.name}/wire.jsonl")
    print(f"Dernier paquet : {latest.isoformat()} "
          "(la fenêtre est ancrée sur ce paquet, pas sur l'heure PC).")
    show_summary(result, args.top)
    show_key_samples(result, args.key, args.depth)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
