#!/usr/bin/env bash
#
# Write the Tauri updater manifest of one desktop build flavour, on stdout.
#
# Usage: desktop-manifest.sh <dist dir> <file stem> <version> <download base URL> <notes>
#
# Reads `<stem>.app.tar.gz.sig` and `<stem>.exe.sig` from the dist directory: the
# signatures of the archives the updater installs, which it checks against
# `plugins.updater.pubkey`. Both macOS keys point at the one universal archive.
set -euo pipefail

dist="$1" stem="$2" version="$3" base="$4" notes="$5"

jq -n \
  --arg version "${version}" --arg notes "${notes}" \
  --arg date "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg macSig "$(cat "${dist}/${stem}.app.tar.gz.sig")" --arg macUrl "${base}/${stem}.app.tar.gz" \
  --arg winSig "$(cat "${dist}/${stem}.exe.sig")" --arg winUrl "${base}/${stem}.exe" \
  '{version: $version, notes: $notes, pub_date: $date, platforms: {
    "darwin-aarch64": {signature: $macSig, url: $macUrl},
    "darwin-x86_64": {signature: $macSig, url: $macUrl},
    "windows-x86_64": {signature: $winSig, url: $winUrl}
  }}'
