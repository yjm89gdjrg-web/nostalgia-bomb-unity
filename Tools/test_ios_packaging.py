#!/usr/bin/env python3
"""Synthetic shell-CLI packaging tests, NOT Unity/Xcode compilation or device tests."""
from pathlib import Path
import plistlib
import stat
import struct
import subprocess
import tempfile
import unittest
import zipfile

SCRIPT = Path(__file__).with_name('package_unsigned_ios.sh')
BUNDLE = 'com.originalprototypes.nostalgiabomb'


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ios packaging ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.products = self.root / 'DerivedData/Build/Products'
        self.app = self.products / 'Release-iphoneos/Actual Product Name.app'
        self.output = self.root / 'artifacts/NostalgiaBomb-UNSIGNED.ipa'
        self.make_app(self.app)

    def make_app(self, path):
        path.mkdir(parents=True)
        info = {
            'CFBundleIdentifier': BUNDLE,
            'CFBundleShortVersionString': '0.1.0',
            'CFBundleVersion': '1',
            'CFBundleExecutable': 'ActualGame',
            'CFBundlePackageType': 'APPL',
            'CFBundleSupportedPlatforms': ['iPhoneOS'],
            'MinimumOSVersion': '15.0',
            'UIDeviceFamily': [1],
        }
        (path / 'Info.plist').write_bytes(plistlib.dumps(info, fmt=plistlib.FMT_BINARY))
        # A minimal arm64 MH_EXECUTE header + LC_BUILD_VERSION for iOS.
        # Deliberately no executable game code: this fixture cannot launch.
        binary = struct.pack('<8I', 0xFEEDFACF, 0x0100000C, 0, 2, 1, 24, 0, 0)
        binary += struct.pack('<6I', 0x32, 24, 2, 15 << 16, 15 << 16, 0)
        (path / 'ActualGame').write_bytes(binary)
        (path / 'ActualGame').chmod(0o755)
        (path / 'Data').mkdir()
        (path / 'Data/fixture.bytes').write_bytes(b'original fixture data')

    def update_info(self, **changes):
        path = self.app / 'Info.plist'
        info = plistlib.loads(path.read_bytes())
        info.update(changes)
        path.write_bytes(plistlib.dumps(info))

    def run_packager(self, *extra, output=None):
        return subprocess.run(
            ['bash', str(SCRIPT), str(self.products), str(output or self.output),
             BUNDLE, '0.1.0', *extra], text=True, capture_output=True,
        )

    def assert_rejected(self, message, *extra, output=None):
        result = self.run_packager(*extra, output=output)
        self.assertNotEqual(result.returncode, 0, result.stdout)
        self.assertIn(message, result.stderr)
        self.assertFalse(self.output.exists())

    def test_packages_actual_app_with_binary_plist_and_valid_payload(self):
        result = self.run_packager('1')
        self.assertEqual(result.returncode, 0, result.stderr)
        with zipfile.ZipFile(self.output) as archive:
            self.assertIsNone(archive.testzip())
            prefix = 'Payload/Actual Product Name.app/'
            self.assertEqual(set(archive.namelist()), {
                prefix + 'Info.plist', prefix + 'ActualGame', prefix + 'Data/fixture.bytes',
            })
            info = plistlib.loads(archive.read(prefix + 'Info.plist'))
            self.assertEqual(info['CFBundleIdentifier'], BUNDLE)
            self.assertEqual(info['CFBundleShortVersionString'], '0.1.0')
            self.assertEqual(info['CFBundleVersion'], '1')
            mode = archive.getinfo(prefix + 'ActualGame').external_attr >> 16
            self.assertTrue(mode & stat.S_IXUSR)
            self.assertEqual(archive.read(prefix + 'Data/fixture.bytes'), b'original fixture data')
        self.assertIn('UNSIGNED', result.stdout)
        self.assertIn('arm64', result.stdout)

    def test_preserves_internal_symlinks_and_empty_directories(self):
        (self.app / 'Data/alias.bytes').symlink_to('fixture.bytes')
        (self.app / 'Empty').mkdir()
        result = self.run_packager()
        self.assertEqual(result.returncode, 0, result.stderr)
        with zipfile.ZipFile(self.output) as archive:
            prefix = 'Payload/Actual Product Name.app/'
            link = archive.getinfo(prefix + 'Data/alias.bytes')
            self.assertTrue(stat.S_ISLNK(link.external_attr >> 16))
            self.assertEqual(archive.read(link), b'fixture.bytes')
            self.assertIn(prefix + 'Empty/', archive.namelist())

    def test_ignores_simulator_products_and_nested_apps(self):
        self.make_app(self.products / 'Release-iphonesimulator/Simulator.app')
        self.make_app(self.app / 'PlugIns/Nested.app')
        result = self.run_packager()
        self.assertEqual(result.returncode, 0, result.stderr)
        with zipfile.ZipFile(self.output) as archive:
            self.assertFalse(any('Simulator.app' in name for name in archive.namelist()))

    def test_requires_one_actual_release_device_app(self):
        self.make_app(self.products / 'Release-iphoneos/Another.app')
        self.assert_rejected('found 2')

    def test_rejects_missing_device_products(self):
        self.app.rename(self.app.with_suffix('.not-app'))
        self.assert_rejected('found 0')

    def test_rejects_wrong_bundle_and_marketing_version(self):
        self.update_info(CFBundleIdentifier='com.example.wrong')
        self.assert_rejected('Bundle identifier mismatch')
        self.update_info(CFBundleIdentifier=BUNDLE, CFBundleShortVersionString='9.9.9')
        self.assert_rejected('Marketing version mismatch')

    def test_rejects_missing_or_wrong_build_version(self):
        self.update_info(CFBundleVersion='')
        self.assert_rejected('CFBundleVersion')
        self.update_info(CFBundleVersion='1')
        self.assert_rejected('Build number mismatch', '2')

    def test_rejects_invalid_plist(self):
        (self.app / 'Info.plist').write_bytes(b'not a plist')
        self.assert_rejected('Packaging failed:')

    def test_rejects_simulator_platform_plist(self):
        self.update_info(CFBundleSupportedPlatforms=['iPhoneSimulator'])
        self.assert_rejected('not an iOS device app')

    def test_accepts_fat_arm64_device_macho(self):
        path = self.app / 'ActualGame'
        binary = path.read_bytes()
        fat = struct.pack('>7I', 0xCAFEBABE, 1, 0x0100000C, 0, 28, len(binary), 0)
        path.write_bytes(fat + binary)
        result = self.run_packager()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_accepts_older_ios_minimum_version_load_command(self):
        path = self.app / 'ActualGame'
        header = struct.pack('<8I', 0xFEEDFACF, 0x0100000C, 0, 2, 1, 16, 0, 0)
        path.write_bytes(header + struct.pack('<4I', 0x25, 16, 15 << 16, 15 << 16))
        result = self.run_packager()
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_rejects_malformed_load_commands(self):
        path = self.app / 'ActualGame'
        data = bytearray(path.read_bytes())
        struct.pack_into('<I', data, 36, 1000)
        path.write_bytes(data)
        self.assert_rejected('Invalid Mach-O command size')

    def test_rejects_non_arm64_executable(self):
        path = self.app / 'ActualGame'
        data = bytearray(path.read_bytes())
        struct.pack_into('<I', data, 4, 0x01000007)  # x86_64.
        path.write_bytes(data)
        self.assert_rejected('must be arm64')

    def test_rejects_arm64_simulator_macho_despite_device_plist(self):
        path = self.app / 'ActualGame'
        data = bytearray(path.read_bytes())
        struct.pack_into('<I', data, 40, 7)  # LC_BUILD_VERSION iOS Simulator.
        path.write_bytes(data)
        self.assert_rejected('iOS device platform')

    def test_rejects_truncated_or_non_macho_executable(self):
        (self.app / 'ActualGame').write_bytes(b'not executable code')
        self.assert_rejected('Mach-O header')

    def test_rejects_missing_or_non_executable_binary(self):
        (self.app / 'ActualGame').chmod(0o644)
        self.assert_rejected('not executable')
        (self.app / 'ActualGame').unlink()
        self.assert_rejected('executable is missing')

    def test_rejects_unsafe_executable_name(self):
        self.update_info(CFBundleExecutable='../outside')
        self.assert_rejected('Unsafe CFBundleExecutable')

    def test_rejects_signing_and_provisioning_material(self):
        signature = self.app / '_CodeSignature'
        signature.mkdir()
        self.assert_rejected('Signing/provisioning material')
        signature.rmdir()
        (self.app / 'embedded.mobileprovision').write_bytes(b'fixture')
        self.assert_rejected('Signing/provisioning material')

    def test_rejects_macho_code_signature_command(self):
        path = self.app / 'ActualGame'
        data = bytearray(path.read_bytes())
        struct.pack_into('<II', data, 16, 2, 40)  # Two commands, 40 bytes.
        data += struct.pack('<4I', 0x1D, 16, 0, 0)
        path.write_bytes(data)
        self.assert_rejected('code signature')

    def test_rejects_escaping_symlink(self):
        (self.root / 'outside').write_bytes(b'secret fixture')
        (self.app / 'Data/escape').symlink_to(self.root / 'outside')
        self.assert_rejected('Unsafe or broken application symlink')

    def test_requires_unsigned_output_name(self):
        other = self.output.with_name('NostalgiaBomb.ipa')
        self.assert_rejected('clearly named', output=other)
        self.assertFalse(other.exists())

    def test_does_not_overwrite_existing_output(self):
        self.output.parent.mkdir()
        self.output.write_bytes(b'existing artifact')
        result = self.run_packager()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('refusing to overwrite', result.stderr)
        self.assertEqual(self.output.read_bytes(), b'existing artifact')


if __name__ == '__main__':
    unittest.main(verbosity=2)
