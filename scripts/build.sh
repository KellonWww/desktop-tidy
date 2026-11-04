#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# Keep Git Bash on the same production build path as the Windows installer.
export MSYS2_ARG_CONV_EXCL='*'
powershell.exe -NoProfile -File "$(cygpath -w scripts/build.ps1)" -BuildDir "$(cygpath -w "${BUILD_DIR:-build/release}")"
