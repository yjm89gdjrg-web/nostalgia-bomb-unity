#!/usr/bin/env bash
# Offline tests for Tools/archive_input.sh. No network and no curl calls.
# Usage: bash Tools/test_archive_input.sh
set -u

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=archive_input.sh
source "$here/archive_input.sh"

pass=0
fail=0

expect_ok() {
  if "$@" >/dev/null 2>&1; then
    pass=$((pass + 1))
  else
    printf 'FAIL (expected accept): %s\n' "$*"
    fail=$((fail + 1))
  fi
}

expect_bad() {
  if "$@" >/dev/null 2>&1; then
    printf 'FAIL (expected reject): %s\n' "$*"
    fail=$((fail + 1))
  else
    pass=$((pass + 1))
  fi
}

# --- URL acceptance ---
expect_ok a2d_validate_url "https://example.com/x.tar.gz"
expect_ok a2d_validate_url "https://files.example.org:8443/a/NostalgiaBomb-Xcode.tar.gz"
expect_ok a2d_validate_url "https://bucket.s3.amazonaws.com/obj?X-Amz-Signature=abc123"
expect_ok a2d_validate_url "https://1.2.3.4/x.tgz"
# --- URL rejection ---
expect_bad a2d_validate_url ""
expect_bad a2d_validate_url "http://example.com/x.tar.gz"
expect_bad a2d_validate_url "ftp://example.com/x"
expect_bad a2d_validate_url "file:///etc/passwd"
expect_bad a2d_validate_url "https://user:pass@example.com/x"
expect_bad a2d_validate_url "https://user@example.com/x"
expect_bad a2d_validate_url "https://"
expect_bad a2d_validate_url "https:///x"
expect_bad a2d_validate_url "https://example.com/a b.tar.gz"

# --- SHA-256 format ---
HEX64="$(printf 'a%.0s' {1..64})"
expect_ok a2d_validate_sha256 "$HEX64"
expect_ok a2d_validate_sha256 "$(printf '%s' "$HEX64" | tr 'a-f' 'A-F')"
expect_ok a2d_validate_sha256 "$(printf '0123456789abcdef%.0s' {1..4})"
expect_bad a2d_validate_sha256 ""
expect_bad a2d_validate_sha256 "abc"
expect_bad a2d_validate_sha256 "$(printf 'a%.0s' {1..63})"
expect_bad a2d_validate_sha256 "$(printf 'a%.0s' {1..65})"
expect_bad a2d_validate_sha256 "$(printf 'g%.0s' {1..64})"

# --- digest verification against a real file ---
tmp="$(mktemp -d "${TMPDIR:-/tmp}/archive input.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
printf 'unity xcode export fixture' > "$tmp/archive.tar.gz"
sum="$(shasum -a 256 "$tmp/archive.tar.gz" | awk '{print $1}')"
expect_ok a2d_verify_sha256 "$tmp/archive.tar.gz" "$sum"
expect_ok a2d_verify_sha256 "$tmp/archive.tar.gz" "$(printf '%s' "$sum" | tr 'a-f' 'A-F')"
expect_bad a2d_verify_sha256 "$tmp/archive.tar.gz" "$(printf 'b%.0s' {1..64})"
expect_bad a2d_verify_sha256 "$tmp/missing.tar.gz" "$sum"
expect_bad a2d_verify_sha256 "$tmp/archive.tar.gz" "short"

printf 'archive_input tests: %d passed, %d failed\n' "$pass" "$fail"
[[ "$fail" -eq 0 ]]
