#!/usr/bin/env python3
"""Fail-closed extractor for a Unity iOS Xcode export tar.gz.

This is deliberately NOT `tar -xzf` and NOT `tarfile.extractall()`. It extracts
only after validating every member, into a destination that must not already
exist, and removes all partial output if anything is unsafe.

Guarantees:
  * Only regular files, directories, symbolic links and hard links are written.
    Device nodes, FIFOs and other special members are rejected outright.
  * Member names must be relative; absolute names, NUL bytes, backslashes and
    any ".." (or empty) path component are rejected.
  * Symbolic-link targets must be relative and must resolve inside the
    extraction root (framework-style links such as
    "A.framework/Versions/Current -> A" and "../" hops that stay inside are
    allowed). Absolute or escaping link targets are rejected.
  * Hard-link targets must reference an in-archive, non-escaping path.
  * Nothing is ever written *through* an extracted symlink: a member whose
    parent path was itself created as a symlink is rejected.
  * setuid/setgid/sticky bits are stripped; ordinary executable bits are kept.
    Directories always keep owner rwx so their contents stay reachable.
  * Extraction is bounded by member-count and total-byte limits.
  * After extraction every resulting real path is re-verified to be inside the
    destination, and symlinks must resolve to existing in-tree targets.

Environment overrides (for tests): SAFE_EXTRACT_MAX_MEMBERS,
SAFE_EXTRACT_MAX_TOTAL_BYTES, SAFE_EXTRACT_MAX_MEMBER_BYTES.

Usage: python3 safe_extract_tar.py ARCHIVE.tar.gz DEST_DIR
"""
from __future__ import annotations

import os
import shutil
import stat
import sys
import tarfile

DEFAULT_MAX_MEMBERS = 200_000
DEFAULT_MAX_TOTAL_BYTES = 8 * 1024 ** 3
DEFAULT_MAX_MEMBER_BYTES = 4 * 1024 ** 3

REGULAR_TYPES = (tarfile.REGTYPE, tarfile.AREGTYPE)


class UnsafeArchive(Exception):
    """Raised when the archive (or the destination) violates the policy."""


def _limits():
    def read(name, default):
        raw = os.environ.get(name)
        if raw is None or raw == "":
            return default
        try:
            value = int(raw)
        except ValueError:
            raise UnsafeArchive(f"{name} is not an integer: {raw!r}")
        if value <= 0:
            raise UnsafeArchive(f"{name} must be positive")
        return value

    return {
        "members": read("SAFE_EXTRACT_MAX_MEMBERS", DEFAULT_MAX_MEMBERS),
        "total": read("SAFE_EXTRACT_MAX_TOTAL_BYTES", DEFAULT_MAX_TOTAL_BYTES),
        "member": read("SAFE_EXTRACT_MAX_MEMBER_BYTES", DEFAULT_MAX_MEMBER_BYTES),
    }


def _within(path, root):
    try:
        return os.path.commonpath([str(path), str(root)]) == str(root)
    except ValueError:
        return False


def _clean_name(name):
    """Normalise a member name; return None for the root-dir marker."""
    if not isinstance(name, str) or name == "":
        raise UnsafeArchive("empty member name")
    if "\x00" in name:
        raise UnsafeArchive("NUL byte in member name")
    if "\\" in name:
        raise UnsafeArchive(f"backslash in member name: {name!r}")
    if name.startswith("/"):
        raise UnsafeArchive(f"absolute member name: {name!r}")
    clean = name
    while clean.startswith("./"):
        clean = clean[2:]
    clean = clean.rstrip("/")
    if clean in ("", "."):
        return None
    parts = clean.split("/")
    for part in parts:
        if part in ("", ".."):
            raise UnsafeArchive(f"unsafe path component in {name!r}")
    return clean


def _check_link_target(member, link):
    """Validate a symlink (member-relative) or hardlink (root-relative) target.

    `member` is the cleaned member name for symlinks, or None for hard links.
    """
    if not isinstance(link, str) or link == "":
        raise UnsafeArchive(f"empty link target in {member!r}")
    if "\x00" in link:
        raise UnsafeArchive(f"NUL byte in link target in {member!r}")
    if "\\" in link:
        raise UnsafeArchive(f"backslash in link target in {member!r}")
    if link.startswith("/"):
        raise UnsafeArchive(f"absolute link target escapes root: {link!r}")
    # Normalise against the member's directory (symlink semantics) or the root.
    if member is None:  # hard link: archive-root relative
        base = ""
    else:
        base = os.path.dirname(member)
    resolved = os.path.normpath(os.path.join(base, link))
    if resolved == ".." or resolved.startswith("../") or resolved.startswith("/"):
        raise UnsafeArchive(f"link target escapes root: {member!r} -> {link!r}")
    return resolved


def _validate_all(tar, limits):
    """Validate every header before a single byte is written."""
    members = tar.getmembers()
    if len(members) > limits["members"]:
        raise UnsafeArchive(
            f"archive has {len(members)} members; limit is {limits['members']}"
        )
    total = 0
    symlink_names = set()
    seen_names = set()
    planned = []
    for member in members:
        clean = _clean_name(member.name)
        if clean is None:
            continue
        if clean in seen_names:
            raise UnsafeArchive(f"duplicate member path: {clean!r}")
        seen_names.add(clean)
        kind = member.type
        if kind == tarfile.DIRTYPE:
            pass
        elif kind in REGULAR_TYPES:
            size = int(member.size)
            if size < 0 or size > limits["member"]:
                raise UnsafeArchive(f"member too large: {clean!r} ({size} bytes)")
            total += size
            if total > limits["total"]:
                raise UnsafeArchive("archive uncompressed size exceeds limit")
        elif kind == tarfile.SYMTYPE:
            _check_link_target(clean, member.linkname)
        elif kind == tarfile.LNKTYPE:
            _check_link_target(None, member.linkname)
        else:
            raise UnsafeArchive(
                f"unsupported member type {kind!r} for {clean!r} "
                "(device, FIFO or other special file)"
            )
        # Reject writing through a symlink created earlier in the archive.
        parts = clean.split("/")
        for index in range(1, len(parts)):
            if "/".join(parts[:index]) in symlink_names:
                raise UnsafeArchive(
                    f"refusing to write {clean!r} through an extracted symlink"
                )
        if kind == tarfile.SYMTYPE:
            symlink_names.add(clean)
        planned.append((member, clean))
    return planned, total


def _write_member(tar, member, clean, dest, limits, counters, dir_modes):
    target = os.path.join(dest, clean)
    if not _within(target, dest):
        raise UnsafeArchive(f"member escapes root: {clean!r}")
    parent = os.path.dirname(target)
    if parent:
        os.makedirs(parent, exist_ok=True)
    mode = stat.S_IMODE(member.mode) & 0o777  # strip setuid/setgid/sticky
    kind = member.type
    if kind == tarfile.DIRTYPE:
        if os.path.islink(target):
            raise UnsafeArchive(f"directory member collides with symlink: {clean!r}")
        os.makedirs(target, exist_ok=True)
        # Owner rwx is forced so a malformed archive cannot yield an
        # unreadable/unreachable tree; the archive's other bits are kept.
        dir_modes.append((target, mode | 0o700))
        counters["dirs"] += 1
    elif kind in REGULAR_TYPES:
        if os.path.lexists(target) and os.path.islink(target):
            raise UnsafeArchive(f"file member collides with symlink: {clean!r}")
        source = tar.extractfile(member)
        if source is None:
            raise UnsafeArchive(f"cannot read regular member: {clean!r}")
        written = 0
        with source, open(target, "wb") as out:
            while True:
                chunk = source.read(1024 * 1024)
                if not chunk:
                    break
                written += len(chunk)
                if written > limits["member"]:
                    raise UnsafeArchive(f"member exceeded size limit: {clean!r}")
                out.write(chunk)
        counters["bytes"] += written
        if counters["bytes"] > limits["total"]:
            raise UnsafeArchive("extracted size exceeds limit")
        os.chmod(target, mode or 0o644)
        counters["files"] += 1
    elif kind == tarfile.SYMTYPE:
        if os.path.lexists(target):
            raise UnsafeArchive(f"symlink member collides with existing path: {clean!r}")
        os.symlink(member.linkname, target)
        counters["symlinks"] += 1
    elif kind == tarfile.LNKTYPE:
        link = member.linkname
        if link.startswith("./"):
            link = link[2:]
        link = link.rstrip("/")
        source = os.path.join(dest, link)
        if (not _within(source, dest) or os.path.islink(source)
                or not os.path.isfile(source)):
            raise UnsafeArchive(f"unresolved or unsafe hard link: {clean!r}")
        if os.path.lexists(target):
            raise UnsafeArchive(f"hard link collides with existing path: {clean!r}")
        os.link(source, target)
        counters["hardlinks"] += 1
    else:  # pragma: no cover - validated earlier
        raise UnsafeArchive(f"unsupported member type for {clean!r}")


def _post_validate(dest):
    root = os.path.realpath(dest)
    for dirpath, dirnames, filenames in os.walk(dest, followlinks=False):
        for name in list(dirnames) + list(filenames):
            path = os.path.join(dirpath, name)
            if os.path.islink(path):
                resolved = os.path.realpath(path)
                if not _within(resolved, root):
                    raise UnsafeArchive(f"symlink escapes root: {path!r}")
                if not os.path.exists(path):
                    raise UnsafeArchive(f"broken symlink: {path!r}")
            else:
                resolved = os.path.realpath(path)
                if not _within(resolved, root):
                    raise UnsafeArchive(f"path escapes root: {path!r}")


def extract(archive, dest, limits=None):
    limits = limits or _limits()
    archive = os.path.abspath(archive)
    dest = os.path.abspath(dest)
    if not os.path.isfile(archive):
        raise UnsafeArchive(f"archive not found: {archive}")
    if os.path.lexists(dest):
        raise UnsafeArchive(f"destination already exists: {dest}")
    os.makedirs(os.path.dirname(dest) or ".", exist_ok=True)
    os.mkdir(dest)
    counters = {"files": 0, "dirs": 0, "symlinks": 0, "hardlinks": 0, "bytes": 0}
    dir_modes = []
    try:
        with tarfile.open(archive, "r:gz") as tar:
            planned, _ = _validate_all(tar, limits)
            for member, clean in planned:
                _write_member(tar, member, clean, dest, limits, counters, dir_modes)
        for path, mode in sorted(dir_modes, key=lambda item: item[0].count(os.sep),
                                  reverse=True):
            os.chmod(path, mode)
        _post_validate(dest)
    except BaseException:
        shutil.rmtree(dest, ignore_errors=True)
        raise
    counters["root"] = dest
    return counters


def main(argv):
    argv = sys.argv if argv is None else argv
    if len(argv) != 3:
        print("Usage: safe_extract_tar.py ARCHIVE.tar.gz DEST_DIR", file=sys.stderr)
        return 2
    try:
        counters = extract(argv[1], argv[2])
    except (UnsafeArchive, tarfile.TarError, OSError, EOFError) as error:
        print(f"Safe extraction failed: {error}", file=sys.stderr)
        return 1
    print(
        "Safe extraction OK: {files} files, {dirs} dirs, {symlinks} symlinks, "
        "{hardlinks} hardlinks, {bytes} bytes -> {root}".format(**counters)
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
