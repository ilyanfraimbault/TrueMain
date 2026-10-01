#!/usr/bin/env bash
# Builds the macOS capture helper and the spike runner from source, then runs
# the spike (#1745). Arguments go to the spike: --resolution, --fps, --no-audio,
# --window-id, --out.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
desktop="$(dirname "$here")"

swift build -c release --package-path "$here/macos"
helper="$(swift build -c release --package-path "$here/macos" --show-bin-path)/truemain-capture"
cargo build --release --manifest-path "$desktop/Cargo.toml" -p capture-spike

exec "$desktop/target/release/truemain-capture-spike" --helper "$helper" "$@"
