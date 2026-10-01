#!/usr/bin/env python3
"""Validate and locate a Unity-generated Xcode export after safe extraction.

This checks the archive's extracted shape before xcodebuild is allowed to read
it. It does not prove that the project came from Unity, compiles, signs, or
runs. The archive's SHA-256 must be checked independently by the workflow.

Usage: python3 validate_xcode_export.py EXTRACTED_ROOT
Prints the single project root on stdout.
"""
from __future__ import annotations

import os
import stat
import sys
from pathlib import Path


class InvalidExport(ValueError):
    pass


def within(path: Path, root: Path) -> bool:
    try:
        return os.path.commonpath([str(path), str(root)]) == str(root)
    except ValueError:
        return False


def validate(root_arg: str) -> Path:
    root = Path(root_arg).resolve()
    if not root.is_dir() or root.is_symlink():
        raise InvalidExport("extracted root is not a real directory")

    projects = []
    for dirpath, dirnames, filenames in os.walk(root, followlinks=False):
        current = Path(dirpath)
        # Check every symlink without following it; safe_extract_tar already
        # checked these too, but this is a deliberate second boundary before
        # xcodebuild consumes the tree.
        for name in list(dirnames) + list(filenames):
            path = current / name
            if path.is_symlink():
                resolved = path.resolve()
                if not within(resolved, root):
                    raise InvalidExport(f"symlink escapes extracted root: {path}")
                if not path.exists():
                    raise InvalidExport(f"broken symlink in export: {path}")
        dirnames[:] = [name for name in dirnames if not (current / name).is_symlink()]
        for name in dirnames:
            if name == "Unity-iPhone.xcodeproj":
                projects.append(current / name)

    if len(projects) != 1:
        raise InvalidExport(
            f"expected exactly one real Unity-iPhone.xcodeproj; found {len(projects)}"
        )
    project = projects[0]
    if project.is_symlink() or not project.is_dir():
        raise InvalidExport("Unity-iPhone.xcodeproj must be a real directory")
    pbxproj = project / "project.pbxproj"
    if not pbxproj.is_file() or pbxproj.is_symlink():
        raise InvalidExport("Unity-iPhone.xcodeproj/project.pbxproj is missing or symlinked")
    marker = project.parent / "EXPORT-ONLY-NOT-IPA.txt"
    if not marker.is_file() or marker.is_symlink():
        raise InvalidExport(
            "Unity export marker EXPORT-ONLY-NOT-IPA.txt is missing beside the project"
        )
    marker_text = marker.read_text(encoding="utf-8", errors="replace")
    if "No Xcode compilation" not in marker_text or "device testing" not in marker_text:
        raise InvalidExport("Unity export marker does not describe an export-only project")

    # The root and project path are safe to pass as shell arguments; print only
    # the resolved path, never archive URL or member contents.
    return project.parent


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("Usage: validate_xcode_export.py EXTRACTED_ROOT", file=sys.stderr)
        return 2
    try:
        project_root = validate(argv[1])
    except (InvalidExport, OSError) as error:
        print(f"Xcode export validation failed: {error}", file=sys.stderr)
        return 1
    print(project_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
