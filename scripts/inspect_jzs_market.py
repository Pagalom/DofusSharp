#!/usr/bin/env python3
"""Read-only local characterization of Dofus jzs market messages.

No captured payloads are copied, exported or written. Only a restricted list
of numeric protobuf fields is printed. This is not a price decoder.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
from pathlib import Path
import sys
import time
from typing import NamedTuple


class Field(NamedTuple):
    number: int
    wire_type: int
    value: int | bytes


def read_varint(data: bytes, pos: int) -> tuple[int, int]:
    value = 0
    for shift in range(0, 70, 7):
        if pos >= len(data):
            raise ValueError("truncated varint")
        byte = data[pos]
        pos += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            if value > (1 << 64) - 1:
                raise ValueError("out-of-range varint")
            return value, pos
    raise ValueError("oversized varint")


def fields(data: bytes) -> list[Field]:
    pos = 0
    parsed: list[Field] = []
    if len(data) > 16 * 1024 * 1024:
        raise ValueError("message too large")
    while pos < len(data):
        tag, pos = read_varint(data, pos)
        num, kind = tag >> 3, tag & 7
        if num == 0 or num > 536870911:
            raise ValueError("invalid protobuf tag")
        if kind == 0:
            value, pos = read_varint(data, pos)
        elif kind == 2:
            length, pos = read_varint(data, pos)
            if pos + length > len(data):
                raise ValueError("truncated bytes")
            value = data[pos:pos + length]
            pos += length
        elif kind == 1:
            if pos + 8 > len(data):
                raise ValueError("truncated fixed64")
            value = int.from_bytes(data[pos:pos + 8], "little")
            pos += 8
        elif kind == 5:
            if pos + 4 > len(data):
                raise ValueError("truncated fixed32")
            value = int.from_bytes(data[pos:pos + 4], "little")
            pos += 4
        else:
            raise ValueError("unsupported wire type")
        parsed.append(Field(num, kind, value))
    return parsed


def varints(parsed: list[Field], number: int) -> list[int]:
    return [int(f.value) for f in parsed if f.number == number and f.wire_type == 0]


def opaque_numeric_summary(data: bytes) -> dict:
    """Describe possible numeric structure, without revealing opaque bytes.

    A valid nested protobuf is just a structural hypothesis; its integers
    are *not* verified prices. If protobuf parsing fails, expose only a
    bounded list of raw varints, likewise without price semantics.
    """
    if len(data) > 64:
        return {"kind": "too_long", "size": len(data)}

    try:
        nested = fields(data)
    except ValueError:
        nested = None

    if nested:
        entries = []
        for field in nested[:12]:
            if field.wire_type == 2:
                entries.append(f"f{field.number}:bytes({len(field.value)})")
            else:
                entries.append(f"f{field.number}:wire{field.wire_type}={field.value}")
        return {"kind": "nested?", "values": entries,
                "truncated": len(nested) > 12}

    packed = []
    offset = 0
    try:
        while offset < len(data) and len(packed) < 12:
            value, offset = read_varint(data, offset)
            packed.append(value)
    except ValueError:
        return {"kind": "unrecognized", "size": len(data)}
    if offset != len(data):
        return {"kind": "unrecognized", "size": len(data)}
    return {"kind": "raw_varints?", "values": packed}



def read_four_numeric_positions(data: bytes) -> list[int] | None:
    """Read four raw protobuf varints, without assigning lot semantics."""
    values = []
    offset = 0
    try:
        while offset < len(data) and len(values) <= 4:
            value, offset = read_varint(data, offset)
            values.append(value)
    except ValueError:
        return None
    if len(values) != 4 or offset != len(data):
        return None
    return values


def summarize_lot_candidates(body: bytes) -> dict:
    """Aggregate observed packed f2 positions, without claiming lot sizes.

    A candidate result is complete only when all rows match the root item
    and grouping, and each has exactly four numeric positions. Reject
    partial aggregation: it could report a false market minimum.
    """
    root = fields(body)
    item_ids = varints(root, 3)
    categories = varints(root, 2)
    rows = [field for field in root if field.number == 1]
    result = {
        "rows": len(rows),
        "decoded_rows": 0,
        "complete": False,
        "positive_minima": None,
        "nonzero_counts": None,
    }
    if (len(item_ids) != 1 or item_ids[0] == 0 or
            len(categories) != 1 or categories[0] == 0 or not rows):
        return result

    values_by_row = []
    for row in rows:
        if row.wire_type != 2:
            return result
        try:
            nested = fields(row.value)
        except ValueError:
            return result

        if (varints(nested, 1) != item_ids or
                varints(nested, 4) != categories):
            return result

        packed = [f.value for f in nested if f.number == 2]
        if (len(packed) != 1 or
                next(f.wire_type for f in nested if f.number == 2) != 2):
            return result

        slots = read_four_numeric_positions(packed[0])
        if slots is None:
            return result
        values_by_row.append(slots)
        result["decoded_rows"] += 1

    result["complete"] = True
    result["positive_minima"] = [
        min((row[i] for row in values_by_row if row[i] > 0), default=None)
        for i in range(4)
    ]
    result["nonzero_counts"] = [
        sum(row[i] > 0 for row in values_by_row)
        for i in range(4)
    ]
    return result


def jzs_summary(body: bytes, inspect_f2: bool = False,
                lot_summary: bool = False) -> dict:
    root = fields(body)
    offers = []
    invalid_rows = 0
    for field in root:
        if field.number != 1 or field.wire_type != 2:
            continue
        try:
            nested = fields(field.value)
        except ValueError:
            invalid_rows += 1
            continue
        # f2 opaque bytes are intentionally neither decoded nor printed.
        opaque_f2 = [f.value for f in nested if f.number == 2 and f.wire_type == 2]
        offers.append({
            "f1": varints(nested, 1),
            "f3": varints(nested, 3),
            "f4": varints(nested, 4),
            "f2_lengths": [len(v) for v in opaque_f2],
            **({"f2_numeric_hypotheses": [opaque_numeric_summary(v)
                                           for v in opaque_f2]}
               if inspect_f2 else {}),
        })
    result = {
        "root_f2": varints(root, 2),
        "item_f3": varints(root, 3),
        "offers": offers,
        "invalid_rows": invalid_rows,
    }
    if lot_summary:
        result["lot_candidate"] = summarize_lot_candidates(body)
    return result


def latest_capture() -> Path:
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise FileNotFoundError(
            "LOCALAPPDATA absent : renseigner --file explicitement"
        )
    parent = Path(local) / "BestCrush" / "DebugCaptures" / "Network"
    captures = sorted(parent.glob("session-*/wire.jsonl"),
                      key=lambda p: p.stat().st_mtime, reverse=True)
    if not captures:
        raise FileNotFoundError(
            f"Aucun wire.jsonl sous {parent}. "
            "Activer la conservation des artefacts de debug."
        )
    return captures[0]


def read_jzs(line: str, inspect_f2: bool = False,
             lot_summary: bool = False) -> tuple[str, dict] | None:
    try:
        record = json.loads(line)
        if record.get("key") != "jzs" or record.get("direction") != "S→C":
            return None
        body = base64.b64decode(record["bodyBase64"], validate=True)
        summary = jzs_summary(body, inspect_f2=inspect_f2,
                              lot_summary=lot_summary)
        stamp = str(record.get("utc", "?"))
        return stamp, summary
    except (ValueError, TypeError, KeyError, json.JSONDecodeError):
        return None


def display(stamp: str, summary: dict, max_rows: int) -> None:
    # f3 failed the real-HDV price comparison (Tongues Wabbits:
    # 4899 kamas displayed, minimum f3=7529). Never label it a price.
    root_id = summary["item_f3"]
    root_f2 = summary["root_f2"]
    offers = summary["offers"]
    print(f"jzs {stamp} | item f3={root_id} | root f2={root_f2}"
          f" | offers={len(offers)} | invalid={summary['invalid_rows']}")
    for index, offer in enumerate(offers[:max_rows], 1):
        print(f"    row {index:02d}: f1={offer['f1']} "
              f"offer.f3={offer['f3']} f4={offer['f4']} "
              f"f2 byte lengths={offer['f2_lengths']}")
        for number, hypothesis in enumerate(
                offer.get("f2_numeric_hypotheses", []), 1):
            print(f"        f2[{number}] {hypothesis['kind']}: "
                  f"{hypothesis.get('values', 'non-decoded')}"
                  f"{' (truncated)' if hypothesis.get('truncated') else ''}")
    if len(offers) > max_rows:
        print(f"    ... {len(offers) - max_rows} autres lignes")
    candidates = [v for o in offers for v in o["f3"] if v > 0]
    if candidates:
        print(f"    minimum offer.f3={min(candidates)} (NE PAS utiliser comme prix)")
    if "lot_candidate" in summary:
        lot = summary["lot_candidate"]
        if lot["complete"]:
            minima = [str(value) if value is not None else "—"
                      for value in lot["positive_minima"]]
            counts = lot["nonzero_counts"]
            print(f"    Positions candidates f2[1..4] : {minima}")
            print(f"    Lignes non nulles par position : {counts}"
                  f" / {lot['rows']}")
            print("    HYPOTHÈSES uniquement : aucune quantité de lot "
                  "ni aucun prix validé ici.")
        else:
            print(f"    f2 non caractérisable intégralement "
                  f"({lot['decoded_rows']}/{lot['rows']} lignes) : "
                  "aucun minimum proposé.")
    print()


def iter_existing(path: Path, last: int, inspect_f2: bool = False,
                  item_id: int | None = None, lot_summary: bool = False):
    matched: list[tuple[str, dict]] = []
    with path.open("r", encoding="utf-8-sig") as source:
        for line in source:
            record = read_jzs(line, inspect_f2=inspect_f2,
                              lot_summary=lot_summary)
            if record is not None and (
                    item_id is None or record[1]["item_f3"] == [item_id]):
                matched.append(record)
                if len(matched) > last:
                    matched.pop(0)
    yield from matched


def watch(path: Path, max_rows: int, inspect_f2: bool = False,
          item_id: int | None = None, lot_summary: bool = False) -> None:
    # Watch only newly appended records, not past messages. Ctrl+C to stop.
    with path.open("r", encoding="utf-8-sig") as source:
        source.seek(0, os.SEEK_END)
        while True:
            line = source.readline()
            if not line:
                time.sleep(0.3)
                continue
            record = read_jzs(line, inspect_f2=inspect_f2,
                              lot_summary=lot_summary)
            if record is not None and (
                    item_id is None or record[1]["item_f3"] == [item_id]):
                display(record[0], record[1], max_rows)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--file", type=Path, help="wire.jsonl à analyser")
    parser.add_argument("--last", type=int, default=8,
                        help="nombre de derniers jzs à afficher (défaut 8)")
    parser.add_argument("--rows", type=int, default=6,
                        help="offres affichées par message (défaut 6)")
    parser.add_argument("--watch", action="store_true",
                        help="attendre de nouveaux jzs jusqu'à Ctrl+C")
    parser.add_argument("--inspect-f2", action="store_true",
                        help="décrire les entiers possibles du champ f2 "
                             "(aucun prix confirmé)")
    parser.add_argument("--item-id", type=int,
                        help="filtrer les réponses jzs sur un ID d'objet")
    parser.add_argument("--lot-summary", action="store_true",
                        help="afficher les minima positifs par position f2 ; "
                             "lots x1/x10/x100/x1000 NON confirmés")
    args = parser.parse_args()
    if not 1 <= args.last <= 100 or not 1 <= args.rows <= 50:
        parser.error("--last doit être entre 1 et 100 et --rows entre 1 et 50")
    if args.item_id is not None and args.item_id <= 0:
        parser.error("--item-id doit être strictement positif")
    try:
        capture = args.file or latest_capture()
        if not capture.is_file():
            raise FileNotFoundError(str(capture))
        print(f"Diagnostic local en lecture seule : {capture.name}")
        print("offer.f3 ne correspond pas au minimum HDV observé "
              "(Tongues Wabbits). Aucune écriture SQLite ou JSONL.\n")
        if args.watch:
            print("Survole / sélectionne un objet en HDV. Ctrl+C pour arrêter.\n")
            watch(capture, args.rows, args.inspect_f2, args.item_id,
                  args.lot_summary)
        else:
            rows = list(iter_existing(capture, args.last,
                                      args.inspect_f2, args.item_id,
                                      args.lot_summary))
            if not rows:
                print("Aucun message jzs S→C trouvé dans ce fichier.")
            for stamp, summary in rows:
                display(stamp, summary, args.rows)
    except FileNotFoundError as exc:
        print(exc, file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("\nDiagnostic arrêté.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
