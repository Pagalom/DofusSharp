"""Synthetic-only tests for inspect_jzs_market.py (no capture files)."""
from __future__ import annotations

import base64
import importlib.util
import json
from pathlib import Path
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

    def test_rejects_overflow_and_invalid_tags(self):
        with self.assertRaises(ValueError):
            market.fields(b"\x00")
        with self.assertRaises(ValueError):
            market.fields(b"\xff" * 11)


if __name__ == "__main__":
    unittest.main()
