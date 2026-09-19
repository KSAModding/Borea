#!/usr/bin/env python3
"""Fetches the icon of a listing and verifies it against its record, per RFC 0058 and RFC 0065.

The author chooses the URL, so a fetch uses HTTPS only, follows at most three redirects,
connects only to a public address it resolved and checked itself, sends no credentials,
and stops reading at the byte cap.
The format and the pixel size come from the bytes, never from the URL or the headers.
"""

from __future__ import annotations

import hashlib
import http.client
import io
import ipaddress
import socket
import ssl
import struct
import time
import urllib.parse
from dataclasses import dataclass

USER_AGENT = "KSAModding-Borea-share-pages"
MAX_REDIRECTS = 3
TIMEOUT = 10
DEADLINE = 30
CHUNK = 65536
CAP = 256 * 1024
SHORTER_MIN = 256
SHORTER_MAX = 1024
RATIO = 2
REDIRECTS = (301, 302, 303, 307, 308)
EXTENSIONS = {"PNG": "png", "JPEG": "jpg", "WebP": "webp"}
JPEG_FRAMES = frozenset((0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF))
EMBEDS_IPV4 = (
    ipaddress.ip_network("64:ff9b::/96"),
    ipaddress.ip_network("::/96"),
    ipaddress.ip_network("::ffff:0:0:0/96"),
)


class IconError(Exception):
    """The icon cannot be used, and the page shows the placeholder."""


@dataclass(frozen=True)
class Facts:
    format: str
    width: int
    height: int
    animated: bool


def public(address: str) -> bool:
    """Whether `address` is a public unicast address, including any IPv4 address it embeds."""
    ip = ipaddress.ip_address(address)
    if ip.version == 6:
        embedded = ip.ipv4_mapped or ip.sixtofour or (ip.teredo[1] if ip.teredo else None)
        if embedded is None and any(ip in network for network in EMBEDS_IPV4):
            embedded = ipaddress.IPv4Address(int(ip) & 0xFFFFFFFF)
        if embedded is not None and not public(str(embedded)):
            return False
        if ip.is_site_local:
            return False
    return ip.is_global and not ip.is_multicast


def resolve(host: str, port: int) -> list[str]:
    try:
        answers = socket.getaddrinfo(host, port, type=socket.SOCK_STREAM)
    except (socket.gaierror, UnicodeError) as error:
        raise IconError(f"{host} does not resolve: {error}") from error
    return [answer[4][0] for answer in answers]


def countdown(deadline: float, timeout: float = TIMEOUT, clock=time.monotonic):
    """A function that gives the seconds one wait may take, and raises when the fetch has none left."""
    ends = clock() + deadline

    def remaining() -> float:
        left = ends - clock()
        if left <= 0:
            raise IconError("the time limit for the icon ran out")
        return min(timeout, left)

    return remaining


class BoundedReader(io.RawIOBase):
    def __init__(self, sock, remaining):
        self.sock = sock
        self.remaining = remaining

    def readable(self):
        return True

    def readinto(self, buffer):
        self.sock.settimeout(self.remaining())
        return self.sock.recv_into(buffer)

    def makefile(self, mode):
        return io.BufferedReader(self)


class PinnedConnection(http.client.HTTPSConnection):
    """HTTPS to one checked address, with the certificate verified for the host name."""

    def __init__(self, host, port, address, remaining):
        self.tls = ssl.create_default_context()
        super().__init__(host, port, context=self.tls)
        self.address = address
        self.remaining = remaining

    def connect(self):
        raw = socket.create_connection((self.address, self.port), self.remaining())
        try:
            raw.settimeout(self.remaining())
            self.sock = self.tls.wrap_socket(raw, server_hostname=self.host)
        except BaseException:
            raw.close()
            raise

    def response_class(self, sock, *arguments, **options):
        return http.client.HTTPResponse(BoundedReader(sock, self.remaining), *arguments, **options)


def target(url: str) -> tuple[str, int, str]:
    """The host, port and request path of `url`. Raises IconError when it may not be fetched."""
    try:
        parts = urllib.parse.urlsplit(url)
        host = parts.hostname
        port = parts.port or 443
    except ValueError as error:
        raise IconError(f"{url} is not a valid URL: {error}") from error
    if parts.scheme.lower() != "https":
        raise IconError(f"{url} is not an https URL")
    if parts.username is not None or parts.password is not None:
        raise IconError(f"{url} carries credentials")
    if not host or not host.isascii():
        raise IconError(f"{url} names no host that can be fetched")
    path = parts.path or "/"
    if parts.query:
        path += "?" + parts.query
    if not path.isascii():
        raise IconError(f"{url} has a path that is not ASCII")
    return host, port, path


def fetch(url: str, cap: int = CAP, *, resolve=resolve, connect=PinnedConnection, clock=time.monotonic,
          timeout: float = TIMEOUT, deadline: float = DEADLINE) -> bytes:
    """The bytes at `url`, at most `cap` of them. Raises IconError."""
    remaining = countdown(deadline, timeout, clock)
    current = url
    for _ in range(MAX_REDIRECTS + 1):
        body, location = _request(current, cap, resolve, connect, remaining)
        if location is None:
            return body
        current = location
    raise IconError(f"{url} is reached through more than {MAX_REDIRECTS} redirects")


def _request(url, cap, resolve, connect, remaining):
    host, port, path = target(url)
    addresses = resolve(host, port)
    if not addresses:
        raise IconError(f"{host} resolves to no address")
    for address in addresses:
        if not public(address):
            raise IconError(f"{host} resolves to {address}, which is not a public address")

    try:
        connection = _open(host, port, addresses, connect, remaining)
        try:
            connection.request("GET", path, headers={
                "User-Agent": USER_AGENT,
                "Accept": "image/png, image/jpeg, image/webp",
                "Accept-Encoding": "identity",
            })
            return _answer(url, connection.getresponse(), cap, remaining)
        finally:
            connection.close()
    except (OSError, ValueError, http.client.HTTPException) as error:
        raise IconError(f"{host} did not answer: {error!r}") from error


def _open(host, port, addresses, connect, remaining):
    failure = None
    for address in addresses:
        remaining()
        connection = connect(host, port, address, remaining)
        try:
            connection.connect()
            return connection
        except ssl.SSLCertVerificationError:
            connection.close()
            raise
        except OSError as error:
            connection.close()
            failure = error
    raise failure


def _answer(url, response, cap, remaining):
    status = response.status
    if status in REDIRECTS:
        location = response.getheader("Location")
        if not location:
            raise IconError(f"{url} answered HTTP {status} with no Location")
        return None, urllib.parse.urljoin(url, location.strip())
    if status != 200:
        raise IconError(f"{url} answered HTTP {status}")

    encoding = (response.getheader("Content-Encoding") or "identity").strip().lower()
    if encoding != "identity":
        raise IconError(f"{url} is served with Content-Encoding {encoding}")
    length = (response.getheader("Content-Length") or "").strip()
    if length.isdecimal() and int(length) > cap:
        raise IconError(f"{url} is {length} bytes, above the cap of {cap}")

    body = bytearray()
    while True:
        remaining()
        chunk = response.read(min(CHUNK, cap + 1 - len(body)))
        if not chunk:
            return bytes(body), None
        body += chunk
        if len(body) > cap:
            raise IconError(f"{url} is larger than the cap of {cap} bytes")


def inspect(data: bytes) -> Facts:
    """The format, pixel size and animation of `data`. Raises IconError."""
    try:
        if data.startswith(b"\x89PNG\r\n\x1a\n"):
            return _png(data)
        if data.startswith(b"\xff\xd8\xff"):
            return _jpeg(data)
        if data[:4] == b"RIFF" and data[8:12] == b"WEBP":
            return _webp(data)
    except (struct.error, IndexError) as error:
        raise IconError(f"the image ends early: {error}") from error
    raise IconError("the bytes are not PNG, JPEG or WebP")


def _png(data):
    position = 8
    size = None
    animated = False
    pixels = False
    while position + 12 <= len(data):
        length, kind = struct.unpack(">I4s", data[position:position + 8])
        body = data[position + 8:position + 8 + length]
        if len(body) < length:
            break
        if size is None:
            if kind != b"IHDR" or length != 13:
                raise IconError("the PNG does not start with its header chunk")
            size = struct.unpack(">II", body[:8])
        elif kind == b"acTL":
            animated = True
        elif kind == b"IDAT":
            pixels = True
        elif kind == b"IEND":
            if not pixels:
                raise IconError("the PNG carries no image data")
            return Facts("PNG", size[0], size[1], animated)
        position += 12 + length
    raise IconError("the PNG ends before its IEND chunk")


def _jpeg(data):
    position = 2
    while position < len(data):
        if data[position] != 0xFF:
            raise IconError("the JPEG has a broken marker")
        while position < len(data) and data[position] == 0xFF:
            position += 1
        if position >= len(data):
            break
        marker = data[position]
        position += 1
        if marker == 0x01 or 0xD0 <= marker <= 0xD8:
            continue
        if marker in (0xD9, 0xDA):
            break
        length = struct.unpack(">H", data[position:position + 2])[0]
        if length < 2:
            raise IconError("the JPEG has a broken segment length")
        if marker in JPEG_FRAMES:
            height, width = struct.unpack(">HH", data[position + 3:position + 7])
            return Facts("JPEG", width, height, False)
        position += length
    raise IconError("the JPEG names no frame size before its image data")


def _webp(data):
    end = 8 + struct.unpack("<I", data[4:8])[0]
    if end > len(data):
        raise IconError("the WebP is shorter than its RIFF header says")
    position = 12
    canvas = None
    frame = None
    animated = False
    while position + 8 <= end:
        kind = data[position:position + 4]
        length = struct.unpack("<I", data[position + 4:position + 8])[0]
        if position + 8 + length > end:
            raise IconError("a WebP chunk runs past the end of the file")
        body = data[position + 8:position + 8 + length]
        if kind == b"VP8X":
            if length < 10:
                raise IconError("the WebP has a broken extended header")
            animated = animated or bool(body[0] & 0x02)
            canvas = (1 + int.from_bytes(body[4:7], "little"), 1 + int.from_bytes(body[7:10], "little"))
        elif kind in (b"ANIM", b"ANMF"):
            animated = True
        elif kind == b"VP8 " and frame is None:
            if length < 10 or body[3:6] != b"\x9d\x01\x2a":
                raise IconError("the WebP has a broken lossy frame header")
            width, height = struct.unpack("<HH", body[6:10])
            frame = (width & 0x3FFF, height & 0x3FFF)
        elif kind == b"VP8L" and frame is None:
            if length < 5 or body[0] != 0x2F:
                raise IconError("the WebP has a broken lossless header")
            bits = int.from_bytes(body[1:5], "little")
            frame = (1 + (bits & 0x3FFF), 1 + ((bits >> 14) & 0x3FFF))
        position += 8 + length + (length & 1)

    if frame is None and not animated:
        raise IconError("the WebP carries no image")
    width, height = canvas or frame or (0, 0)
    return Facts("WebP", width, height, animated)


def verify(record: dict, data: bytes) -> str:
    """The file extension for verified icon bytes. Raises IconError when the bytes do not match the record."""
    if len(data) > CAP:
        raise IconError(f"the icon is larger than the cap of {CAP} bytes")
    facts = inspect(data)
    if facts.animated:
        raise IconError(f"the {facts.format} is animated")
    shorter, longer = sorted((facts.width, facts.height))
    if not SHORTER_MIN <= shorter <= SHORTER_MAX or longer > RATIO * shorter:
        raise IconError(f"{facts.width} by {facts.height} pixels is outside the icon limits")
    for key, found in (("width", facts.width), ("height", facts.height), ("size", len(data))):
        if record.get(key) != found:
            raise IconError(f"{key} is {record.get(key)!r} and the bytes show {found}")
    digest = hashlib.sha256(data).hexdigest()
    if str(record.get("sha256") or "").lower() != digest:
        raise IconError(f"sha256 is {record.get('sha256')!r} and the bytes show {digest}")
    return EXTENSIONS[facts.format]
