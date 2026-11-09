"""Read-only access to Valve VPK v1/v2 archives and Source 2 resource references.

Standard library only. Used by extract-resources.py.
"""
import os
import struct
import zlib

VPK_SIGNATURE = 0x55AA1234
EMBEDDED_ARCHIVE = 0x7FFF


class VpkEntry:
    __slots__ = ("path", "crc", "archive", "offset", "length", "preload")

    def __init__(self, path, crc, archive, offset, length, preload):
        self.path, self.crc, self.archive = path, crc, archive
        self.offset, self.length, self.preload = offset, length, preload

    @property
    def size(self):
        return self.length + len(self.preload)


class Vpk:
    """A VPK directory file (`*_dir.vpk`) or a single-file VPK such as a map."""

    def __init__(self, path):
        self.path = path
        self.folder = os.path.dirname(path)
        name = os.path.basename(path)
        self.prefix = name[:-len("_dir.vpk")] if name.endswith("_dir.vpk") else None
        self.entries = {}
        self._read_tree()

    def _read_tree(self):
        with open(self.path, "rb") as f:
            signature, version, tree_size = struct.unpack("<III", f.read(12))
            if signature != VPK_SIGNATURE:
                raise ValueError(f"{self.path}: not a VPK file")
            if version == 2:
                f.read(16)
                header = 28
            elif version == 1:
                header = 12
            else:
                raise ValueError(f"{self.path}: unsupported VPK version {version}")
            tree = f.read(tree_size)
        self.data_offset = header + tree_size
        pos = 0

        def read_string():
            nonlocal pos
            end = tree.index(b"\0", pos)
            value = tree[pos:end].decode("utf-8", "replace")
            pos = end + 1
            return value

        while True:
            extension = read_string()
            if not extension:
                break
            while True:
                folder = read_string()
                if not folder:
                    break
                while True:
                    name = read_string()
                    if not name:
                        break
                    crc, preload_size, archive, offset, length, _ = struct.unpack_from("<IHHIIH", tree, pos)
                    pos += 18
                    preload = tree[pos:pos + preload_size]
                    pos += preload_size
                    path = name if extension == " " else f"{name}.{extension}"
                    if folder != " ":
                        path = f"{folder}/{path}"
                    self.entries[path] = VpkEntry(path, crc, archive, offset, length, preload)

    def archive_path(self, entry):
        if entry.archive == EMBEDDED_ARCHIVE or self.prefix is None:
            return self.path
        return os.path.join(self.folder, f"{self.prefix}_{entry.archive:03d}.vpk")

    def read(self, entry):
        data = entry.preload
        if entry.length:
            offset = entry.offset + (self.data_offset if entry.archive == EMBEDDED_ARCHIVE or self.prefix is None else 0)
            with open(self.archive_path(entry), "rb") as f:
                f.seek(offset)
                data += f.read(entry.length)
        if len(data) != entry.size:
            raise IOError(f"{entry.path}: expected {entry.size} bytes, read {len(data)}")
        if crc32(data) != entry.crc:
            raise IOError(f"{entry.path}: CRC32 mismatch")
        return data


def crc32(data):
    return zlib.crc32(data) & 0xFFFFFFFF


def external_references(data):
    """Paths from the RERL block of a compiled Source 2 resource (`*_c`)."""
    if len(data) < 16:
        return []
    try:
        _, _, _, block_offset, block_count = struct.unpack_from("<IHHII", data, 0)
        blocks = 8 + block_offset
        for i in range(block_count):
            kind, offset, _ = struct.unpack_from("<4sII", data, blocks + i * 12)
            if kind != b"RERL":
                continue
            base = blocks + i * 12 + 4 + offset
            entries_offset, count = struct.unpack_from("<II", data, base)
            first = base + entries_offset
            result = []
            for j in range(count):
                entry = first + j * 16
                name_offset = struct.unpack_from("<I", data, entry + 8)[0]
                start = entry + 8 + name_offset
                result.append(data[start:data.index(b"\0", start)].decode("utf-8", "replace"))
            return result
    except (struct.error, ValueError):
        pass
    return []


def compiled_path(reference):
    path = reference.lower().replace("\\", "/")
    return path if path.endswith("_c") else path + "_c"
