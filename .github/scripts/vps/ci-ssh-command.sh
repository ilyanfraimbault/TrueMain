#!/usr/bin/env bash
#
# Forced command of the CI deploy key on a VPS (#1089). Installed by hand on
# each host and named in that key's authorized_keys line; CI never runs it
# directly. Install steps and rationale in docs/ci.md ("The CI key's forced
# command").
#
# sshd runs this in place of whatever the client asked for and exposes the
# request in SSH_ORIGINAL_COMMAND. Exactly two requests are accepted:
#
#   "" or "migrate"   exec the migration command with the client's stdin: what
#                     the key ran before this script existed, and what the
#                     rollout's migrate step still sends (no remote command)
#   "pull <tag>"      docker compose pull of the four application images at
#                     <tag> (a git sha or a semver), in the project directory
#
# Anything else is refused with exit 64 and runs nothing. The success line of
# a pull is what the rollout checks for, so a host still running the previous
# forced command (which ignores the request) cannot pass for one that pulled.
#
# Arguments, fixed in authorized_keys and never read from the client:
#   $1  the Docker Manager project directory (holds docker-compose.yml)
#   $2  the migration command (default /usr/local/bin/apply-migration.sh)
set -euo pipefail

project_dir="${1:?usage: ci-ssh-command.sh <project-dir> [migrate-command]}"
migrate_command="${2:-/usr/local/bin/apply-migration.sh}"
request="${SSH_ORIGINAL_COMMAND-}"
tag_pattern='^([0-9a-f]{7,40}|[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?)$'

if [[ -z "$request" || "$request" == "migrate" ]]; then
  exec "$migrate_command"
fi

tag=""
if [[ "$request" =~ ^pull\ (.+)$ ]]; then
  tag="${BASH_REMATCH[1]}"
fi

if [[ -z "$tag" || ! "$tag" =~ $tag_pattern ]]; then
  printf 'ci-ssh-command: refused %q\n' "$request" >&2
  exit 64
fi

cd "$project_dir"
env -i HOME="$HOME" PATH="$PATH" IMAGE_TAG="$tag" \
  docker compose pull api web admin ingestor
echo "ci-ssh-command: pulled ${tag}"
