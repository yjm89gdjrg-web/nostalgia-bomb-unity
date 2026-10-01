#!/usr/bin/env bash
# Sourceable validators for the manual "unsigned IPA from a prebuilt Xcode
# archive" workflow. Pure input handling: no network, no secrets. Kept in a
# library so it can be unit-tested offline by Tools/test_archive_input.sh.
#
# Compatible with the macOS system Bash 3.2 (no ${var,,}, no mapfile).
# Usage:  source Tools/archive_input.sh

a2d_fail() {
  printf 'archive input rejected: %s\n' "$*" >&2
  return 1
}

# Accept only plain, credential-free HTTPS URLs.
a2d_validate_url() {
  local url="${1-}" rest authority
  [[ -n "$url" ]] || { a2d_fail "archive URL is empty"; return 1; }
  case "$url" in
    *[![:print:]]*) a2d_fail "archive URL contains non-printable characters"; return 1 ;;
  esac
  [[ "$url" != *[[:space:]]* ]] || { a2d_fail "archive URL contains whitespace"; return 1; }
  case "$url" in
    https://*) ;;
    *) a2d_fail "archive URL must use https://"; return 1 ;;
  esac
  rest="${url#https://}"
  authority="${rest%%/*}"
  [[ -n "$authority" ]] || { a2d_fail "archive URL has no host"; return 1; }
  [[ "$authority" != *"@"* ]] || { a2d_fail "archive URL must not embed credentials"; return 1; }
  case "$authority" in
    *"?"*|*"#"*) a2d_fail "archive URL has an invalid authority"; return 1 ;;
  esac
  return 0
}

# Require exactly 64 hexadecimal characters (any case).
a2d_validate_sha256() {
  local sum="${1-}"
  [[ -n "$sum" ]] || { a2d_fail "expected SHA-256 is empty"; return 1; }
  case "$sum" in
    *[!0-9a-fA-F]*) a2d_fail "expected SHA-256 must be 64 hex characters"; return 1 ;;
  esac
  [[ "${#sum}" -eq 64 ]] || { a2d_fail "expected SHA-256 must be 64 hex characters (got ${#sum})"; return 1; }
  return 0
}

# Compare a file against an expected hex digest; case-insensitive.
a2d_verify_sha256() {
  local file="${1-}" expected="${2-}" actual lower
  a2d_validate_sha256 "$expected" || return 1
  [[ -f "$file" ]] || { a2d_fail "archive file not found: $file"; return 1; }
  if command -v shasum >/dev/null 2>&1; then
    actual="$(shasum -a 256 "$file" | awk '{print $1}')"
  elif command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "$file" | awk '{print $1}')"
  else
    a2d_fail "neither shasum nor sha256sum is available"; return 1
  fi
  lower="$(printf '%s' "$expected" | tr '[:upper:]' '[:lower:]')"
  [[ "$actual" == "$lower" ]] || { a2d_fail "SHA-256 mismatch (expected $lower, got $actual)"; return 1; }
  return 0
}

# Download over HTTPS into $2. Redirects must stay HTTPS; size/time bounded.
# The URL itself is never echoed here so a pre-signed URL is not logged.
a2d_download() {
  local url="${1-}" out="${2-}"
  [[ -n "$out" ]] || { a2d_fail "missing download output path"; return 1; }
  command -v curl >/dev/null 2>&1 || { a2d_fail "curl is required"; return 1; }
  if ! curl --fail --show-error --silent --location \
      --proto '=https' --proto-redir '=https' \
      --tlsv1.2 --max-redirs 3 \
      --connect-timeout 30 --max-time 1800 \
      --max-filesize 4294967296 \
      --output "$out" "$url"; then
    a2d_fail "download failed"
    return 1
  fi
  [[ -s "$out" ]] || { a2d_fail "downloaded file is empty"; return 1; }
  return 0
}
