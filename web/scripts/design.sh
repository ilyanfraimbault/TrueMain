#!/usr/bin/env bash
# Design exploration (prototype): run one variant on its own port, on real
# production data. Usage: scripts/design.sh <a|b|c|d|e|f|g|h>
set -euo pipefail

variant="${1:-}"
case "$variant" in
  a) port=3101 ;; b) port=3102 ;; c) port=3103 ;; d) port=3104 ;;
  e) port=3105 ;; f) port=3106 ;; g) port=3107 ;; h) port=3108 ;;
  *) echo "usage: $0 <a|b|c|d|e|f|g|h>" >&2; exit 1 ;;
esac

cd "$(dirname "$0")/.."

pids=$(lsof -ti "tcp:$port" -sTCP:LISTEN || true)
if [ -n "$pids" ]; then kill $pids; sleep 1; fi

export NUXT_PUBLIC_DESIGN_VARIANT="$variant"
export NUXT_BUILD_DIR=".nuxt-$variant"
export NUXT_VITE_CACHE_DIR="node_modules/.cache/vite-$variant"
export NUXT_API_BASE_URL="https://truemain.lol/api"

echo "Variant $variant → http://localhost:$port"
exec npm run dev -- --port "$port"
