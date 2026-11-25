#!/usr/bin/env bash
# Build from WSL using the Windows C# compiler, then (with --install) copy to
# %LOCALAPPDATA%\AlwaysOnKeys so it runs from a normal Windows folder.
set -euo pipefail
cd "$(dirname "$0")"
CSC=/mnt/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
mkdir -p dist
srcs=(); for f in src/*.cs; do srcs+=("$(wslpath -w "$f")"); done
"$CSC" /nologo /target:winexe /platform:x64 /optimize+ /unsafe \
  "/out:$(wslpath -w dist/AlwaysOnKeys.exe)" /r:System.Windows.Forms.dll /r:System.Drawing.dll "${srcs[@]}"
echo "built dist/AlwaysOnKeys.exe ($(stat -c %s dist/AlwaysOnKeys.exe) bytes)"
if [[ "${1:-}" == "--install" ]]; then
  dest="$(wslpath "$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')")/AlwaysOnKeys"
  mkdir -p "$dest"
  cp dist/AlwaysOnKeys.exe "$dest/"
  echo "installed to $(wslpath -w "$dest")\\AlwaysOnKeys.exe"
fi
