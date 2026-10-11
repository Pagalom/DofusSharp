"""Synthetic-only tests for inspect_jzs_market.py (no capture files)."""
from __future__ import annotations

import base64
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


MODULE = Path(__file__).with_name("inspect_jzs_market.py")
spec = importlib.util.spec_from_file_location("inspect_jzs_market", MODULE)
assert spec is not None and spec.loader is not None
market = importlib.util.module_from_spec(spec)
spec.loader.exec_module(market)


def v(value: int) -> bytes:
    data = bytearray()
    while value >= 128:
        data.append((value & 0x7F) | 0x80)
        value >>= 7
    data.append(value)
    return bytes(data)


def num(field: int, value: int) -> bytes:
    return v(field << 3) + v(value)


def blob(field: int, data: bytes) -> bytes:
    return v((field << 3) | 2) + v(len(data)) + data


class MarketDiagnosticTests(unittest.TestCase):
    def test_fixture_varint_encodes_terminal_byte_once(self):
        # Protobuf fixtures must use a single final byte (< 0x80).
        examples = (
            (0, "00"),
            (1, "01"),
            (127, "7f"),
            (128, "8001"),
            (12851, "b364"),
            (65535, "ffff03"),
        )
        for number, encoded_hex in examples:
            with self.subTest(number=number):
                encoded = bytes.fromhex(encoded_hex)
                self.assertEqual(v(number), encoded)
                self.assertEqual(market.read_varint(encoded, 0),
                                 (number, len(encoded)))

    def test_characterizes_both_captured_equipment_identifiers(self):
        for item_id, group in ((12851, 9), (1356, 3)):
            row = num(1, item_id) + blob(2, b"\x00\x01\x02\x03\x04")
            row += num(3, 66305) + num(4, group)
            body = blob(1, row) + num(2, group) + num(3, item_id)
            result = market.jzs_summary(body)
            self.assertEqual(result["item_f3"], [item_id])
            self.assertEqual(result["root_f2"], [group])
            self.assertEqual(result["offers"], [{
                "f1": [item_id],
                "f3": [66305],
                "f4": [group],
                "f2_lengths": [5],
            }])

    def test_valid_no_offer_selection(self):
        result = market.jzs_summary(num(2, 9) + num(3, 12851))
        self.assertEqual(result["offers"], [])
        self.assertEqual(result["item_f3"], [12851])

    def test_truncated_nested_offer_is_not_misread_as_price(self):
        result = market.jzs_summary(
            blob(1, b"\x08\x80") + num(2, 9) + num(3, 12851)
        )
        self.assertEqual(result["invalid_rows"], 1)
        self.assertEqual(result["offers"], [])

    def test_malformed_root_is_rejected(self):
        with self.assertRaises(ValueError):
            market.jzs_summary(b"\x1a\x04\x00")

    def test_only_server_to_client_jzs_are_read(self):
        body = num(2, 9) + num(3, 12851)
        value = base64.b64encode(body).decode("ascii")
        for key, direction, accepted in (
            ("jzs", "S→C", True),
            ("jzs", "C→S", False),
            ("jzn", "S→C", False),
        ):
            row = json.dumps({
                "key": key,
                "direction": direction,
                "bodyBase64": value,
                "utc": "2026-10-11T08:00:00Z"
            })
            self.assertEqual(market.read_jzs(row) is not None, accepted)

    def test_opaque_numeric_f2_parses_possible_protobuf_fields(self):
        # A synthetic 5-byte f2 encodes quantity 1 and value 4899.
        # The tool labels these as hypotheses, never confirmed prices.
        blob_data = num(1, 1) + num(2, 4899)
        self.assertEqual(len(blob_data), 5)
        summary = market.opaque_numeric_summary(blob_data)
        self.assertEqual(summary, {
            "kind": "nested?",
            "values": ["f1:wire0=1", "f2:wire0=4899"],
            "truncated": False,
        })

    def test_f2_numeric_fallback_does_not_force_protobuf_interpretation(self):
        summary = market.opaque_numeric_summary(bytes.fromhex("00a326"))
        self.assertEqual(summary,
                         {"kind": "raw_varints?", "values": [0, 4899]})
        self.assertEqual(
            market.opaque_numeric_summary(bytes.fromhex("80")),
            {"kind": "unrecognized", "size": 1})

    def test_optional_f2_hypothesis_does_not_change_default_output(self):
        opaque = num(1, 1) + num(2, 4899)
        row = num(1, 6493) + blob(2, opaque) + num(3, 7559) + num(4, 11)
        body = blob(1, row) + num(2, 11) + num(3, 6493)
        default = market.jzs_summary(body)
        self.assertEqual(default["offers"][0]["f2_lengths"], [5])
        self.assertNotIn("f2_numeric_hypotheses", default["offers"][0])
        extended = market.jzs_summary(body, inspect_f2=True)
        self.assertEqual(extended["offers"][0]["f2_numeric_hypotheses"],
                         [{"kind": "nested?",
                           "values": ["f1:wire0=1", "f2:wire0=4899"],
                           "truncated": False}])
        self.assertEqual(extended["offers"][0]["f3"], [7559])

    def test_local_item_id_filter_only_returns_selected_equipment(self):
        records = []
        for item_id in (1624, 6493, 7107):
            body = num(2, 11) + num(3, item_id)
            records.append(json.dumps({
                "key": "jzs",
                "direction": "S→C",
                "bodyBase64": base64.b64encode(body).decode("ascii"),
                "utc": "2026-10-11T17:19:16Z",
            }))
        with tempfile.TemporaryDirectory() as directory:
            capture = Path(directory) / "wire.jsonl"
            capture.write_text(chr(10).join(records) + chr(10), encoding="utf-8")
            result = list(market.iter_existing(
                capture, last=8, inspect_f2=True, item_id=6493))
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0][1]["item_f3"], [6493])

    def test_lot_positions_aggregate_four_numeric_slots_without_assuming_lots(self):
        body = (blob(1, num(1, 100) + blob(2, v(0) + v(550) +
                    v(0) + v(99000)) + num(3, 501) + num(4, 11)) +
                blob(1, num(1, 100) + blob(2, v(120) + v(500) +
                    v(8000) + v(0)) + num(3, 502) + num(4, 11)) +
                num(2, 11) + num(3, 100))
        summary = market.summarize_lot_candidates(body)
        self.assertEqual(summary, {
            "rows": 2,
            "decoded_rows": 2,
            "complete": True,
            "positive_minima": [120, 500, 8000, 99000],
            "nonzero_counts": [1, 2, 1, 1],
        })

    def test_lot_positions_ignore_zero_and_do_not_assert_prices(self):
        body = (blob(1, num(1, 6493) +
                    blob(2, v(4899) + v(0) + v(0) + v(0)) +
                    num(3, 1234) + num(4, 11)) +
                num(2, 11) + num(3, 6493))
        default = market.jzs_summary(body)
        self.assertNotIn("lot_candidate", default)
        extended = market.jzs_summary(body, lot_summary=True)
        self.assertEqual(extended["lot_candidate"]["positive_minima"],
                         [4899, None, None, None])
        self.assertEqual(extended["lot_candidate"]["nonzero_counts"],
                         [1, 0, 0, 0])

    def test_lot_positions_refuse_partial_rows_without_reporting_minimum(self):
        good = blob(1, num(1, 100) +
                    blob(2, v(200) + v(0) + v(0) + v(0)) +
                    num(3, 1) + num(4, 11))
        bad = blob(1, num(1, 101) +
                   blob(2, v(1) + v(0) + v(0) + v(0)) +
                   num(3, 2) + num(4, 11))
        summary = market.summarize_lot_candidates(
            good + bad + num(2, 11) + num(3, 100))
        self.assertEqual(summary["rows"], 2)
        self.assertEqual(summary["decoded_rows"], 1)
        self.assertFalse(summary["complete"])
        self.assertIsNone(summary["positive_minima"])

    def test_lot_positions_reject_wrong_packed_shape(self):
        for slots in ([100], [100, 0, 0], [100, 0, 0, 0, 0]):
            with self.subTest(slots=slots):
                body = (blob(1, num(1, 100) + blob(2, b"".join(
                    v(value) for value in slots)) +
                    num(3, 5) + num(4, 11)) +
                    num(2, 11) + num(3, 100))
                result = market.summarize_lot_candidates(body)
                self.assertFalse(result["complete"])
                self.assertIsNone(result["positive_minima"])

    def test_lot_positions_reject_truncated_f2_without_inventing_prices(self):
        body = (blob(1, num(1, 100) +
                    blob(2, v(4899) + bytes.fromhex("80")) +
                    num(3, 5) + num(4, 11)) +
                num(2, 11) + num(3, 100))
        result = market.summarize_lot_candidates(body)
        self.assertFalse(result["complete"])
        self.assertIsNone(result["positive_minima"])

    def test_lot_positions_ignore_empty_followup(self):
        response = num(2, 11) + num(3, 6493)
        result = market.jzs_summary(response, lot_summary=True)
        self.assertEqual(result["lot_candidate"], {
            "rows": 0,
            "decoded_rows": 0,
            "complete": False,
            "positive_minima": None,
            "nonzero_counts": None,
        })

    def test_rejects_overflow_and_invalid_tags(self):
        with self.assertRaises(ValueError):
            market.fields(b"\x00")
        with self.assertRaises(ValueError):
            market.fields(b"\xff" * 11)


if __name__ == "__main__":
    unittest.main()
