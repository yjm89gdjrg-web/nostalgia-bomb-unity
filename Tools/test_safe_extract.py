#!/usr/bin/env python3
"""Unit tests for Tools/safe_extract_tar.py.

These build synthetic tar.gz archives in a temporary directory. They use no
network, no Unity and no Xcode; they exercise the fail-closed extraction
policy itself. They are NOT evidence of a real Unity export or Xcode build.
"""
import contextlib
import io
import os
import stat
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import safe_extract_tar as safe  # noqa: E402


def info(name, type=tarfile.REGTYPE, mode=0o644, size=0, linkname=""):
    member = tarfile.TarInfo(name)
    member.type = type
    member.mode = mode
    member.size = size
    member.linkname = linkname
    return member


class ExtractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="safe extract ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.dest = self.root / "out"

    def build(self, entries, name="archive.tar.gz"):
        path = self.root / name
        with tarfile.open(path, "w:gz") as tar:
            for entry in entries:
                if isinstance(entry, tuple):  # (name, mode, bytes)
                    name_, mode_, data = entry
                    tar.addfile(info(name_, mode=mode_, size=len(data)), io.BytesIO(data))
                else:
                    tar.addfile(entry)
        return path

    def run_extract(self, path, dest=None, env=None):
        old = {}
        for key, value in (env or {}).items():
            old[key] = os.environ.get(key)
            os.environ[key] = str(value)
        try:
            return safe.extract(str(path), str(dest or self.dest))
        finally:
            for key, value in old.items():
                if value is None:
                    os.environ.pop(key, None)
                else:
                    os.environ[key] = value

    def assertRejected(self, path, message, dest=None, env=None):
        with self.assertRaises(safe.UnsafeArchive) as ctx:
            self.run_extract(path, dest=dest, env=env)
        self.assertIn(message, str(ctx.exception))
        self.assertFalse((dest or self.dest).exists(), "partial output was not removed")

    # --- happy paths -----------------------------------------------------
    def test_extracts_files_and_preserves_executable_bits(self):
        archive = self.build([
            info("App", tarfile.DIRTYPE, mode=0o755),
            ("App/run", 0o755, b"#!/bin/sh\necho hi\n"),
            ("App/data.bytes", 0o644, b"payload"),
        ])
        counters = self.run_extract(archive)
        self.assertEqual(counters["files"], 2)
        run = self.dest / "App/run"
        self.assertEqual(stat.S_IMODE(run.stat().st_mode), 0o755)
        self.assertEqual(stat.S_IMODE((self.dest / "App/data.bytes").stat().st_mode), 0o644)
        self.assertEqual(run.read_bytes(), b"#!/bin/sh\necho hi\n")

    def test_extracts_unity_like_project_tree(self):
        archive = self.build([
            info("NostalgiaBomb-Xcode", tarfile.DIRTYPE),
            info("NostalgiaBomb-Xcode/Unity-iPhone.xcodeproj", tarfile.DIRTYPE),
            ("NostalgiaBomb-Xcode/Unity-iPhone.xcodeproj/project.pbxproj", 0o644, b"// pbxproj"),
        ])
        self.run_extract(archive)
        self.assertTrue((self.dest / "NostalgiaBomb-Xcode/Unity-iPhone.xcodeproj/project.pbxproj").is_file())

    def test_skips_leading_dot_slash_and_root_marker(self):
        archive = self.build([
            info("./", tarfile.DIRTYPE, mode=0o755),
            info("./App", tarfile.DIRTYPE, mode=0o755),
            ("./App/readme", 0o644, b"x"),
        ])
        self.run_extract(archive)
        self.assertEqual((self.dest / "App/readme").read_bytes(), b"x")

    def test_preserves_internal_relative_symlink(self):
        archive = self.build([
            ("App/real.txt", 0o644, b"data"),
            info("App/link.txt", tarfile.SYMTYPE, linkname="real.txt"),
        ])
        self.run_extract(archive)
        link = self.dest / "App/link.txt"
        self.assertTrue(link.is_symlink())
        self.assertEqual(os.readlink(link), "real.txt")
        self.assertEqual(link.read_bytes(), b"data")

    def test_preserves_framework_style_symlinks(self):
        archive = self.build([
            info("F.framework/Versions/A", tarfile.DIRTYPE, mode=0o755),
            ("F.framework/Versions/A/F", 0o755, b"MACHO"),
            info("F.framework/Versions/Current", tarfile.SYMTYPE, linkname="A"),
            info("F.framework/F", tarfile.SYMTYPE, linkname="Versions/Current/F"),
        ])
        self.run_extract(archive)
        self.assertTrue((self.dest / "F.framework/F").is_file())
        self.assertEqual((self.dest / "F.framework/F").read_bytes(), b"MACHO")

    def test_accepts_internal_hardlink(self):
        archive = self.build([
            ("App/real", 0o644, b"data"),
            info("App/hard", tarfile.LNKTYPE, linkname="App/real"),
        ])
        self.run_extract(archive)
        real = self.dest / "App/real"
        hard = self.dest / "App/hard"
        self.assertTrue(hard.is_file())
        self.assertEqual(os.stat(real).st_ino, os.stat(hard).st_ino)

    def test_strips_setuid_and_setgid_but_keeps_exec(self):
        archive = self.build([("tool", 0o6755, b"bin")])
        self.run_extract(archive)
        self.assertEqual(stat.S_IMODE((self.dest / "tool").stat().st_mode), 0o755)

    # --- unsafe members --------------------------------------------------
    def test_rejects_parent_traversal_member(self):
        archive = self.build([("../evil", 0o644, b"x")])
        self.assertRejected(archive, "unsafe path component")

    def test_rejects_nested_traversal_member(self):
        archive = self.build([("a/../../b", 0o644, b"x")])
        self.assertRejected(archive, "unsafe path component")

    def test_rejects_empty_component_member(self):
        archive = self.build([("a//b", 0o644, b"x")])
        self.assertRejected(archive, "unsafe path component")

    def test_rejects_absolute_member_name(self):
        archive = self.build([("/abs/evil", 0o644, b"x")])
        self.assertRejected(archive, "absolute member name")

    def test_rejects_absolute_symlink_target(self):
        archive = self.build([info("App/link", tarfile.SYMTYPE, linkname="/etc/passwd")])
        self.assertRejected(archive, "absolute link target")

    def test_rejects_escaping_relative_symlink(self):
        archive = self.build([info("App/link", tarfile.SYMTYPE, linkname="../../outside")])
        self.assertRejected(archive, "escapes root")

    def test_rejects_symlink_target_with_backslash(self):
        archive = self.build([info("App/link", tarfile.SYMTYPE, linkname="..\\..\\x")])
        self.assertRejected(archive, "backslash in link target")

    def test_rejects_write_through_symlinked_parent(self):
        archive = self.build([
            info("a", tarfile.DIRTYPE),
            info("a/link", tarfile.SYMTYPE, linkname="sub"),
            ("a/link/evil", 0o644, b"x"),
        ])
        self.assertRejected(archive, "through an extracted symlink")

    def test_rejects_broken_internal_symlink(self):
        archive = self.build([info("App/link", tarfile.SYMTYPE, linkname="missing")])
        self.assertRejected(archive, "broken symlink")

    def test_rejects_escaping_hardlink(self):
        archive = self.build([info("App/x", tarfile.LNKTYPE, linkname="../../etc/passwd")])
        self.assertRejected(archive, "escapes root")

    def test_rejects_hardlink_to_directory(self):
        archive = self.build([
            info("d", tarfile.DIRTYPE),
            info("h", tarfile.LNKTYPE, linkname="d"),
        ])
        self.assertRejected(archive, "unsafe hard link")

    def test_rejects_special_fifo_member(self):
        archive = self.build([info("fifo", tarfile.FIFOTYPE, mode=0o644)])
        self.assertRejected(archive, "unsupported member type")

    # --- limits, destination and cleanup ---------------------------------
    def test_rejects_existing_destination(self):
        self.dest.mkdir()
        (self.dest / "keep").write_text("keep")
        archive = self.build([("f", 0o644, b"x")])
        with self.assertRaises(safe.UnsafeArchive) as ctx:
            self.run_extract(archive)
        self.assertIn("already exists", str(ctx.exception))
        self.assertTrue((self.dest / "keep").exists(), "pre-existing dest was touched")

    def test_removes_output_when_duplicate_is_rejected(self):
        # Duplicate names are rejected during the preflight, before any
        # archive member is written; the destination still must be removed.
        archive = self.build([
            info("a", tarfile.DIRTYPE),
            ("a", 0o644, b"collision"),
        ])
        with self.assertRaises(safe.UnsafeArchive) as ctx:
            self.run_extract(archive)
        self.assertIn("duplicate member path", str(ctx.exception))
        self.assertFalse(self.dest.exists(), "partial output was not removed")

    def test_enforces_member_count_limit(self):
        archive = self.build([
            ("a", 0o644, b"1"),
            ("b", 0o644, b"2"),
            ("c", 0o644, b"3"),
        ])
        self.assertRejected(archive, "members", env={"SAFE_EXTRACT_MAX_MEMBERS": 2})

    def test_enforces_total_size_limit(self):
        archive = self.build([("big", 0o644, b"0123456789")])
        self.assertRejected(archive, "size exceeds limit",
                            env={"SAFE_EXTRACT_MAX_TOTAL_BYTES": 4})

    def test_enforces_single_member_size_limit(self):
        archive = self.build([("big", 0o644, b"0123456789")])
        self.assertRejected(archive, "member too large",
                            env={"SAFE_EXTRACT_MAX_MEMBER_BYTES": 4})

    # --- pure helpers ----------------------------------------------------
    def test_clean_name_rejects_empty_nul_and_backslash(self):
        with self.assertRaises(safe.UnsafeArchive):
            safe._clean_name("")
        with self.assertRaises(safe.UnsafeArchive):
            safe._clean_name("a\x00b")
        with self.assertRaises(safe.UnsafeArchive):
            safe._clean_name("a\\b")
        self.assertIsNone(safe._clean_name("./"))

    def test_cli_exit_codes(self):
        archive = self.build([("f", 0o644, b"x")])
        with contextlib.redirect_stderr(io.StringIO()), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(safe.main(["prog", str(archive), str(self.dest)]), 0)
        bad = self.build([("../evil", 0o644, b"x")], name="bad.tar.gz")
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(safe.main(["prog", str(bad), str(self.root / "out2")]), 1)
        self.assertEqual(safe.main(["prog"]), 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
