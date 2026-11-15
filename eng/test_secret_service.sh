#!/usr/bin/env bash
set -euo pipefail
# Disposable CI keyring, never the runner's default login keyring.
task_secret_root=$(mktemp -d)
trap 'rm -rf "$task_secret_root"' EXIT
mkdir -m 700 "$task_secret_root/data" "$task_secret_root/config" "$task_secret_root/runtime" "$task_secret_root/control"
export XDG_DATA_HOME="$task_secret_root/data"
export XDG_CONFIG_HOME="$task_secret_root/config"
export XDG_RUNTIME_DIR="$task_secret_root/runtime"
export GNOME_KEYRING_CONTROL="$task_secret_root/control"
dbus-run-session -- bash -euo pipefail -c '
  printf %s systemtools-fixture-only | gnome-keyring-daemon --unlock --components=secrets --control-directory "$GNOME_KEYRING_CONTROL"
  dotnet run --project tests/SystemTools.M8.Tests -c Release -- --native-credentials
'
