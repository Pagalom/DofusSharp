"""Synthetic-only regression tests for the passive P3.0 inspector."""
from __future__ import annotations

import base64
from datetime import datetime, timedelta, timezone
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
from contextlib import redirect_stdout

import inspect_crush_network as inspector


T0 = datetime(2026, 10, 11, 18, 0, tzinfo=timezone.utc)


def varint(number: int) -> bytes:
    result = bytearray()
    while number >= 128:
        result.append((number & 127) | 128)
        number >>= 7
    result.append(number)
    return bytes(result)


def numeric(field: int, value: int) -> bytes:
    return varint(field << 3) + varint(value)


def blob(field: int, payload: bytes) -> bytes:
    return varint((field << 3) | 2) + varint(len(payload)) + payload


def make_row(seconds: int, key: str = "abc", direction: str = "S→C",
             body: bytes = b"") -> str:
    return json.dumps({
        "utc": (T0 + timedelta(seconds=seconds)).isoformat(),
        "key": key,
        "direction": direction,
        "bodyLength": len(body),
        "bodyBase64": base64.b64encode(body).decode("ascii"),
    })


class PassiveCrushInspectorTests(unittest.TestCase):
    def test_schema_reads_real_wire_field_names(self):
        record = inspector.parse_record(make_row(1, "kjz", "C→S",
                                                 numeric(1, 456)))
        self.assertIsNotNone(record)
        self.assertEqual(record.key, "kjz")
        self.assertEqual(record.direction, "C→S")
        self.assertEqual(record.utc, T0 + timedelta(seconds=1))
        self.assertEqual(record.body_length, 3)

    def test_malformed_rows_are_skipped(self):
        bad = (
            "not-json", "[]", "{}",
            json.dumps({"utc": "invalid", "direction": "S→C",
                        "key": "abc", "bodyLength": 5}),
            json.dumps({"utc": "2026-10-11T18:00:00",
                        "direction": "S→C",
                        "key": "abc", "bodyLength": 5}),
            json.dumps({"utc": T0.isoformat(), "direction": "S→C",
                        "key": "abc", "bodyLength": -1}),
            json.dumps({"utc": T0.isoformat(), "direction": "UNKNOWN",
                        "key": "abc", "bodyLength": 5}),
        )
        for value in bad:
            with self.subTest(value=value):
                self.assertIsNone(inspector.parse_record(value))

    def test_last_timestamp_is_latest_valid_even_if_unsorted(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            path.write_text("\n".join([
                make_row(80), "invalid", make_row(120), make_row(90)
            ]), encoding="utf-8")
            self.assertEqual(inspector.last_timestamp(path),
                             T0 + timedelta(seconds=120))

    def test_recent_window_omits_older_messages_and_keeps_directions(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            path.write_text("\n".join([
                make_row(0, "abc", "C→S"),
                make_row(65, "xyz", "S→C"),
                make_row(75, "xyz", "S→C"),
                make_row(90, "jzs", "S→C"),
            ]), encoding="utf-8")
            summary = inspector.summarize(
                path, T0 + timedelta(seconds=90), seconds=30,
                bin_seconds=5)
            self.assertEqual(summary["selected"], 3)
            self.assertEqual(summary["totals"][("S→C", "xyz")], 2)
            self.assertEqual(summary["totals"][("C→S", "abc")], 0)
            self.assertEqual(summary["sizes"][("S→C", "xyz")], [0, 0])
            self.assertEqual(sum(
                sum(counter.values())
                for counter in summary["timeline"].values()), 3)

    def test_direction_filter_limits_summary_not_underlying_file(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            path.write_text("\n".join([
                make_row(1, "abc", "C→S"),
                make_row(2, "xyz", "S→C"),
            ]), encoding="utf-8")
            summary = inspector.summarize(
                path, T0 + timedelta(seconds=2), 10, 2,
                direction="C→S")
            self.assertEqual(summary["selected"], 1)
            self.assertEqual(summary["totals"][("C→S", "abc")], 1)

    def test_only_requested_key_is_sampled_and_limit_is_respected(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            payload = numeric(1, 345)
            path.write_text("\n".join([
                make_row(i, "qyz", "S→C", payload)
                for i in range(6)
            ] + [make_row(7, "abc", "C→S", b"x")]),
                encoding="utf-8")
            summary = inspector.summarize(
                path, T0 + timedelta(seconds=7), 20, 5,
                keys={"qyz"}, sample_limit=2)
            self.assertEqual(len(summary["samples"]["qyz"]), 2)
            self.assertEqual(
                [r.utc for r in summary["samples"]["qyz"]],
                [T0 + timedelta(seconds=4), T0 + timedelta(seconds=5)])
            self.assertNotIn("abc", summary["samples"])

    def test_numeric_tree_is_bounded_and_never_prints_raw_bytes(self):
        rune = numeric(1, 123) + numeric(3, 7)
        float32 = varint((2 << 3) | 5) + struct.pack("<f", 0.45)
        result = blob(1, blob(1, rune) + float32 + numeric(3, 900))
        lines = inspector.numeric_tree(result, max_depth=4)
        rendered = "\n".join(lines)
        self.assertIn("varint=123", rendered)
        self.assertIn("varint=7", rendered)
        self.assertIn("float?=0.45", rendered)
        self.assertNotIn("hex=", rendered)
        self.assertNotIn("bodyBase64", rendered)
        self.assertNotIn(base64.b64encode(result).decode(), rendered)

    def test_corrupt_protobuf_never_becomes_confirmed_crush(self):
        self.assertEqual(
            inspector.numeric_tree(bytes.fromhex("0a0680")),
            ["(protobuf non reconnu)"])
        self.assertTrue(all("?" in line or "varint" in line
                            for line in inspector.numeric_tree(
                                blob(1, numeric(1, 5)))))

    def test_summary_does_not_emit_raw_payload_or_semantic_guesses(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            payload = bytes.fromhex("08049601")
            path.write_text(make_row(5, "qyz", "S→C", payload) + "\n",
                            encoding="utf-8")
            summary = inspector.summarize(path, T0 + timedelta(seconds=5),
                                         30, 5)
            output = io.StringIO()
            with redirect_stdout(output):
                inspector.show_summary(summary, 10)
            shown = output.getvalue()
            self.assertIn("qyz", shown)
            self.assertNotIn(base64.b64encode(payload).decode(), shown)
            self.assertNotIn("coefficient confirmé", shown)

    def test_empty_capture_file_has_no_timestamp(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "wire.jsonl"
            path.write_text("", encoding="utf-8")
            self.assertIsNone(inspector.last_timestamp(path))


if __name__ == "__main__":
    unittest.main()
