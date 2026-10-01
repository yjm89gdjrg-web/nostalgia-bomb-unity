#!/usr/bin/env bash
# Package an existing generic-iOS Release .app. Does NOT build, sign or install.
# Usage: bash Tools/package_unsigned_ios.sh PRODUCTS_DIR OUTPUT-UNSIGNED.ipa BUNDLE_ID VERSION [BUILD_NUMBER]
set -euo pipefail
if [[ "$#" -lt 4 || "$#" -gt 5 ]]; then
  echo "Usage: $0 PRODUCTS_DIR OUTPUT-UNSIGNED.ipa BUNDLE_ID VERSION [BUILD_NUMBER]" >&2
  exit 2
fi
python3 - "$@" <<'PY'
from pathlib import Path
import os
import plistlib
import re
import stat
import struct
import sys
import tempfile
import zipfile


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate_executable(data):
    """Read real Mach-O headers; arm64 alone is not proof of an iOS-device build."""
    require(len(data) >= 32, 'Executable is not a complete Mach-O header')
    if data[:4] in (b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf',
                    b'\xbe\xba\xfe\xca', b'\xbf\xba\xfe\xca'):
        endian = '>' if data[:4] in (b'\xca\xfe\xba\xbe', b'\xca\xfe\xba\xbf') else '<'
        fat64 = data[:4] in (b'\xca\xfe\xba\xbf', b'\xbf\xba\xfe\xca')
        count = struct.unpack_from(endian + 'I', data, 4)[0]
        size = 32 if fat64 else 20
        require(0 < count <= 64 and 8 + count * size <= len(data), 'Invalid fat Mach-O table')
        slices = []
        for i in range(count):
            entry = struct.unpack_from(endian + ('IIQQII' if fat64 else 'IIIII'), data, 8 + i * size)
            cpu, offset, length = entry[0], entry[2], entry[3]
            require(cpu == 0x0100000C, 'Executable contains a non-arm64 architecture')
            require(offset >= 8 + count * size and length >= 32 and offset + length <= len(data),
                    'Invalid fat Mach-O slice')
            slices.append(data[offset:offset + length])
    else:
        slices = [data]
    for binary in slices:
        require(binary[:4] == b'\xcf\xfa\xed\xfe', 'Executable is not a little-endian arm64 Mach-O')
        _, cpu, _, filetype, commands, command_bytes, _, _ = struct.unpack_from('<8I', binary)
        require(cpu == 0x0100000C, 'Executable must be arm64, not simulator/x86')
        require(filetype == 2, 'Mach-O executable must have MH_EXECUTE type')
        require(32 + command_bytes <= len(binary), 'Truncated Mach-O load commands')
        pos = 32
        ios_platform = False
        for _ in range(commands):
            require(pos + 8 <= 32 + command_bytes, 'Invalid Mach-O load command')
            cmd, length = struct.unpack_from('<II', binary, pos)
            require(length >= 8 and pos + length <= 32 + command_bytes, 'Invalid Mach-O command size')
            require(cmd != 0x1D, 'Executable has a code signature; expected UNSIGNED input')
            if cmd == 0x32:  # LC_BUILD_VERSION: iOS=2; iOS Simulator=7.
                require(length >= 24, 'Invalid LC_BUILD_VERSION')
                require(struct.unpack_from('<I', binary, pos + 8)[0] == 2,
                        'Executable is not built for the iOS device platform')
                ios_platform = True
            elif cmd in (0x24, 0x25, 0x2F, 0x30):
                require(cmd == 0x25 and length >= 16, 'Executable is not built for iOS')
                ios_platform = True  # Older LC_VERSION_MIN_IPHONEOS.
            pos += length
        require(pos == 32 + command_bytes and ios_platform, 'Missing or inconsistent iOS Mach-O platform')
    return 'arm64'


def validate_info(info, bundle, version, build):
    require(isinstance(info, dict), 'Info.plist is not a dictionary')
    for key in ('CFBundleIdentifier', 'CFBundleShortVersionString', 'CFBundleVersion', 'CFBundleExecutable'):
        require(isinstance(info.get(key), str) and info[key].strip(), f'Missing or empty {key}')
    require(info['CFBundleIdentifier'] == bundle, 'Bundle identifier mismatch')
    require(info['CFBundleShortVersionString'] == version, 'Marketing version mismatch')
    require(re.fullmatch(r'\d+(?:\.\d+){0,2}', info['CFBundleShortVersionString']), 'Invalid marketing version')
    require(re.fullmatch(r'\d+(?:\.\d+){0,2}', info['CFBundleVersion']), 'Invalid build version')
    if build is not None:
        require(info['CFBundleVersion'] == build, 'Build number mismatch')
    require(info.get('CFBundlePackageType') == 'APPL', 'Bundle is not an application')
    require(info.get('CFBundleSupportedPlatforms') == ['iPhoneOS'], 'Bundle is not an iOS device app')
    require(isinstance(info.get('MinimumOSVersion'), str) and
            re.fullmatch(r'\d+(?:\.\d+){0,2}', info['MinimumOSVersion']), 'Invalid MinimumOSVersion')
    executable = info['CFBundleExecutable']
    require(executable not in ('.', '..') and '/' not in executable and '\\' not in executable,
            'Unsafe CFBundleExecutable path')
    return executable


def package():
    products = Path(sys.argv[1]).resolve()
    output = Path(sys.argv[2]).absolute()
    bundle, version = sys.argv[3:5]
    build = sys.argv[5] if len(sys.argv) == 6 else None
    require(products.is_dir(), 'Build products directory is missing')
    require(output.suffix.lower() == '.ipa' and 'UNSIGNED' in output.name,
            'Output must be clearly named *UNSIGNED*.ipa')
    require(not output.exists(), 'Output already exists; refusing to overwrite')
    # Search Release device products, not a hardcoded Unity product name; exclude nested watch/plugin apps.
    candidates = [p for p in products.rglob('*.app')
                  if p.is_dir() and p.parent.name == 'Release-iphoneos'
                  and not any(parent.suffix == '.app' for parent in p.relative_to(products).parents)]
    require(len(candidates) == 1,
            f'Expected exactly one Release-iphoneos .app; found {len(candidates)}')
    app = candidates[0]
    require(not app.is_symlink(), 'Application directory must not be a symlink')
    app = app.resolve()
    require(not output.resolve().is_relative_to(app), 'Output must be outside the application')
    info = plistlib.loads((app / 'Info.plist').read_bytes())
    executable = validate_info(info, bundle, version, build)
    binary = app / executable
    require(binary.is_file() and not binary.is_symlink(), 'Application executable is missing or symlinked')
    require(binary.stat().st_mode & stat.S_IXUSR, 'Application executable is not executable')
    architecture = validate_executable(binary.read_bytes())
    files = sorted(app.rglob('*'))
    for path in files:
        require(path.name not in ('_CodeSignature', 'embedded.mobileprovision'),
                'Signing/provisioning material found; expected UNSIGNED input')
        if path.is_symlink():
            target = os.readlink(path)
            require(not os.path.isabs(target) and path.resolve().is_relative_to(app) and path.exists(),
                    'Unsafe or broken application symlink')
        else:
            require(path.is_dir() or path.is_file(), 'Unsupported special application file')
    output.parent.mkdir(parents=True, exist_ok=True)
    prefix = 'Payload/' + app.name + '/'
    temp_name = None
    try:
        with tempfile.NamedTemporaryFile(dir=output.parent, suffix='.ipa.tmp', delete=False) as temp:
            temp_name = Path(temp.name)
        with zipfile.ZipFile(temp_name, 'w', compression=zipfile.ZIP_DEFLATED, allowZip64=True) as archive:
            for path in files:
                name = prefix + path.relative_to(app).as_posix()
                if path.is_symlink():
                    entry = zipfile.ZipInfo(name)
                    entry.create_system = 3
                    entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                    archive.writestr(entry, os.readlink(path).encode('utf-8'))
                elif path.is_file():
                    archive.write(path, name)
                elif not any(path.iterdir()):
                    archive.write(path, name + '/')
        # Re-open and validate the actual ZIP, not just the pre-copy bundle.
        with zipfile.ZipFile(temp_name) as archive:
            require(archive.testzip() is None, 'IPA ZIP CRC validation failed')
            names = archive.namelist()
            require(len(names) == len(set(names)) and all(name.startswith(prefix) for name in names),
                    'IPA must contain exactly one application under Payload')
            require(all('..' not in Path(name).parts for name in names), 'Unsafe ZIP path')
            packed_info = plistlib.loads(archive.read(prefix + 'Info.plist'))
            validate_info(packed_info, bundle, version, build)
            validate_executable(archive.read(prefix + executable))
            mode = archive.getinfo(prefix + executable).external_attr >> 16
            require(mode & stat.S_IXUSR, 'ZIP lost executable permissions')
        os.replace(temp_name, output)
        temp_name = None
    finally:
        if temp_name is not None:
            temp_name.unlink(missing_ok=True)
    print(f'Validated UNSIGNED IPA: {output}\n'
          f'Bundle={bundle} version={version} build={info["CFBundleVersion"]} '
          f'architecture={architecture} minOS={info["MinimumOSVersion"]}\n'
          'NOT directly installable: Apple signing/provisioning and device validation still required.')


try:
    package()
except (ValueError, OSError, KeyError, struct.error, plistlib.InvalidFileException, zipfile.BadZipFile) as error:
    print(f'Packaging failed: {error}', file=sys.stderr)
    sys.exit(1)
PY
