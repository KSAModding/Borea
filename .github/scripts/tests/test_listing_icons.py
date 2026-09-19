"""Tests for listing_icons.py, with no network."""

from __future__ import annotations

import hashlib
import io
import struct
import sys
import unittest
import zlib
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import listing_icons as icons  # noqa: E402

PUBLIC = "93.184.216.34"


def png_chunk(kind, body):
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))


def png(width, height, animated=False):
    header = png_chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    control = png_chunk(b"acTL", struct.pack(">II", 2, 0)) if animated else b""
    return b"\x89PNG\r\n\x1a\n" + header + control + png_chunk(b"IDAT", zlib.compress(b"\x00")) + png_chunk(b"IEND", b"")


def jpeg(width, height):
    app0 = b"\xff\xe0" + struct.pack(">H", 16) + b"JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00"
    frame = b"\xff\xc0" + struct.pack(">HBHHB", 11, 8, height, width, 1) + b"\x01\x11\x00"
    scan = b"\xff\xda" + struct.pack(">H", 8) + b"\x01\x01\x00\x00\x3f\x00"
    return b"\xff\xd8" + app0 + frame + scan + b"\x00\xff\xd9"


def webp(width, height, animated=False):
    def chunk(kind, body):
        return kind + struct.pack("<I", len(body)) + body + (b"\x00" if len(body) % 2 else b"")

    extended = chunk(b"VP8X", bytes([0x02 if animated else 0, 0, 0, 0])
                     + (width - 1).to_bytes(3, "little") + (height - 1).to_bytes(3, "little"))
    lossless = chunk(b"VP8L", b"\x2f" + struct.pack("<I", (width - 1) | ((height - 1) << 14)))
    body = b"WEBP" + extended + lossless
    return b"RIFF" + struct.pack("<I", len(body)) + body


def record(data, width, height):
    return {"url": "https://example.org/icon", "sha256": hashlib.sha256(data).hexdigest().upper(),
            "width": width, "height": height, "size": len(data)}


class Response:
    def __init__(self, status, body=b"", headers=None):
        self.status = status
        self.body = io.BytesIO(body)
        self.headers = headers or {}

    def getheader(self, name, default=None):
        return next((value for key, value in self.headers.items() if key.lower() == name.lower()), default)

    def read(self, amount):
        return self.body.read(amount)


class Network:
    """DNS and HTTPS from two tables: host to addresses, URL to answer."""

    def __init__(self, addresses, answers):
        self.addresses = addresses
        self.answers = answers
        self.connections = []
        self.requests = []

    def resolve(self, host, port):
        return self.addresses[host]

    def connect(self, host, port, address, remaining):
        network = self

        class Connection:
            def connect(self):
                network.connections.append((host, address))

            def request(self, method, path, headers):
                network.requests.append((host, path, headers))
                self.url = f"https://{host}{path}"

            def getresponse(self):
                answer = network.answers[self.url]
                if isinstance(answer, BaseException):
                    raise answer
                return answer

            def close(self):
                pass

        return Connection()

    def fetch(self, url, cap=icons.CAP):
        return icons.fetch(url, cap, resolve=self.resolve, connect=self.connect)


class Verify(unittest.TestCase):
    def test_png_jpeg_and_webp_give_their_extension(self):
        for data, extension in ((png(512, 512), "png"), (jpeg(300, 300), "jpg"), (webp(256, 256), "webp")):
            with self.subTest(extension=extension):
                facts = icons.inspect(data)
                self.assertEqual(extension, icons.verify(record(data, facts.width, facts.height), data))

    def test_an_icon_that_is_not_square_passes_within_the_ratio(self):
        data = png(1024, 512)
        self.assertEqual("png", icons.verify(record(data, 1024, 512), data))

    def test_sizes_outside_the_icon_limits_fail(self):
        for width, height in ((255, 255), (1025, 1025), (768, 256)):
            data = png(width, height)
            with self.subTest(size=(width, height)), self.assertRaises(icons.IconError):
                icons.verify(record(data, width, height), data)

    def test_an_animated_icon_fails(self):
        for data in (png(256, 256, animated=True), webp(256, 256, animated=True)):
            with self.assertRaises(icons.IconError):
                icons.verify(record(data, 256, 256), data)

    def test_every_record_fact_must_match_the_bytes(self):
        data = png(256, 256)
        for key, value in (("width", 257), ("height", 300), ("size", len(data) + 1), ("sha256", "0" * 64)):
            wrong = {**record(data, 256, 256), key: value}
            with self.subTest(key=key), self.assertRaises(icons.IconError):
                icons.verify(wrong, data)

    def test_bytes_that_are_no_image_fail(self):
        for data in (b"<svg></svg>", b"GIF89a....", png(256, 256)[:30]):
            with self.assertRaises(icons.IconError):
                icons.verify(record(data, 256, 256), data)


class Fetch(unittest.TestCase):
    def test_the_bytes_come_back_from_the_checked_address(self):
        network = Network({"example.org": [PUBLIC]}, {"https://example.org/icon": Response(200, b"image")})

        self.assertEqual(b"image", network.fetch("https://example.org/icon"))
        self.assertEqual([("example.org", PUBLIC)], network.connections)
        headers = network.requests[0][2]
        self.assertNotIn("Cookie", headers)
        self.assertNotIn("Authorization", headers)

    def test_http_is_refused_before_any_lookup(self):
        network = Network({}, {})
        with self.assertRaises(icons.IconError):
            network.fetch("http://example.org/icon")
        self.assertEqual([], network.connections)

    def test_a_private_address_is_refused_before_connecting(self):
        for address in ("127.0.0.1", "10.0.0.1", "169.254.169.254", "::1", "fd00::1", "::ffff:192.168.0.1"):
            network = Network({"example.org": [address]}, {})
            with self.subTest(address=address), self.assertRaises(icons.IconError):
                network.fetch("https://example.org/icon")
            self.assertEqual([], network.connections)

    def test_redirects_are_followed_up_to_three(self):
        answers = {f"https://example.org/{step}": Response(302, headers={"Location": f"/{step + 1}"}) for step in range(3)}
        answers["https://example.org/3"] = Response(200, b"image")
        self.assertEqual(b"image", Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/0"))

        answers["https://example.org/3"] = Response(302, headers={"Location": "/4"})
        with self.assertRaises(icons.IconError):
            Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/0")

    def test_a_redirect_to_http_is_refused(self):
        answers = {"https://example.org/icon": Response(301, headers={"Location": "http://example.org/icon"})}
        with self.assertRaises(icons.IconError):
            Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/icon")

    def test_a_redirect_to_a_broken_or_non_ascii_url_is_refused(self):
        for location in ("https://[bad/x", "/icon-ä.png", "https://example.org:99999/icon"):
            answers = {"https://example.org/icon": Response(302, headers={"Location": location})}
            with self.subTest(location=location), self.assertRaises(icons.IconError):
                Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/icon")

    def test_a_non_ascii_path_is_refused_before_any_lookup(self):
        network = Network({}, {})
        with self.assertRaises(icons.IconError):
            network.fetch("https://example.org/icon-ä.png")
        self.assertEqual([], network.connections)

    def test_a_body_above_the_cap_is_cut_off(self):
        answers = {"https://example.org/icon": Response(200, b"x" * (icons.CAP + 1))}
        with self.assertRaises(icons.IconError):
            Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/icon")

    def test_a_declared_length_above_the_cap_is_refused(self):
        answers = {"https://example.org/icon": Response(200, b"x", {"Content-Length": str(icons.CAP + 1)})}
        with self.assertRaises(icons.IconError):
            Network({"example.org": [PUBLIC]}, answers).fetch("https://example.org/icon")

    def test_an_error_answer_or_a_broken_connection_fails(self):
        for answer in (Response(404), Response(503), ConnectionResetError(), ValueError("bad header")):
            with self.subTest(answer=answer), self.assertRaises(icons.IconError):
                Network({"example.org": [PUBLIC]}, {"https://example.org/icon": answer}).fetch("https://example.org/icon")

    def test_the_deadline_stops_the_fetch(self):
        clock = iter([0, 100, 100, 100])
        with self.assertRaises(icons.IconError):
            icons.fetch("https://example.org/icon", resolve=lambda host, port: [PUBLIC],
                        connect=Network({}, {}).connect, clock=lambda: next(clock), deadline=30)


if __name__ == "__main__":
    unittest.main()
