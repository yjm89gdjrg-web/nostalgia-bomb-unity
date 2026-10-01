#!/usr/bin/env python3
"""Unit tests for the extracted Unity Xcode export shape validator."""
import contextlib
import io
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import validate_xcode_export as validator  # noqa: E402


class ExportValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="xcode export validation ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "extracted"
        self.project_root = self.root / "NostalgiaBomb-Xcode"
        self.project = self.project_root / "Unity-iPhone.xcodeproj"
        self.project.mkdir(parents=True)
        (self.project / "project.pbxproj").write_text("// synthetic pbxproj\n")
        (self.project_root / "EXPORT-ONLY-NOT-IPA.txt").write_text(
            "Unity exported this Xcode project. No Xcode compilation, Apple signing, IPA creation or device testing has occurred.\n"
        )

    def test_accepts_one_real_project_and_marker(self):
        self.assertEqual(validator.validate(str(self.root)), self.project_root.resolve())

    def test_rejects_missing_project(self):
        (self.project / "project.pbxproj").unlink()
        with self.assertRaises(validator.InvalidExport) as ctx:
            validator.validate(str(self.root))
        self.assertIn("project.pbxproj", str(ctx.exception))

    def test_rejects_missing_marker(self):
        (self.project_root / "EXPORT-ONLY-NOT-IPA.txt").unlink()
        with self.assertRaises(validator.InvalidExport) as ctx:
            validator.validate(str(self.root))
        self.assertIn("marker", str(ctx.exception))

    def test_rejects_wrong_marker(self):
        (self.project_root / "EXPORT-ONLY-NOT-IPA.txt").write_text("an IPA was made")
        with self.assertRaises(validator.InvalidExport):
            validator.validate(str(self.root))

    def test_rejects_two_projects(self):
        other = self.root / "Other/Unity-iPhone.xcodeproj"
        other.mkdir(parents=True)
        (other / "project.pbxproj").write_text("// other\n")
        (other.parent / "EXPORT-ONLY-NOT-IPA.txt").write_text(
            "No Xcode compilation or device testing\n"
        )
        with self.assertRaises(validator.InvalidExport) as ctx:
            validator.validate(str(self.root))
        self.assertIn("exactly one", str(ctx.exception))

    def test_rejects_project_symlink(self):
        real = self.root / "real/Unity-iPhone.xcodeproj"
        real.mkdir(parents=True)
        (real / "project.pbxproj").write_text("// real\n")
        (real.parent / "EXPORT-ONLY-NOT-IPA.txt").write_text(
            "No Xcode compilation and device testing\n"
        )
        link_parent = self.root / "link-parent"
        link_parent.mkdir()
        (link_parent / "Unity-iPhone.xcodeproj").symlink_to(real)
        (link_parent / "EXPORT-ONLY-NOT-IPA.txt").write_text(
            "No Xcode compilation and device testing\n"
        )
        with self.assertRaises(validator.InvalidExport):
            validator.validate(str(self.root))

    def test_rejects_escape_symlink(self):
        outside = Path(self.temp.name) / "outside"
        outside.mkdir()
        (self.root / "escape").symlink_to(outside, target_is_directory=True)
        with self.assertRaises(validator.InvalidExport) as ctx:
            validator.validate(str(self.root))
        self.assertIn("escapes", str(ctx.exception))

    def test_rejects_broken_symlink(self):
        (self.root / "broken").symlink_to("missing")
        with self.assertRaises(validator.InvalidExport) as ctx:
            validator.validate(str(self.root))
        self.assertIn("broken", str(ctx.exception))

    def test_cli_statuses(self):
        self.assertEqual(validator.main(["prog", str(self.root)]), 0)
        with contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(validator.main(["prog"]), 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
