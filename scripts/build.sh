#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export MSYS2_ARG_CONV_EXCL='*'
csc="${CSC_EXE:-/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe}"
if [[ ! -f "$csc" ]]; then printf '%s\n' '未找到 Framework x64 编译器，可用 CSC_EXE 指定。' >&2; exit 1; fi
builddir="${BUILD_DIR:-build/prototype}"
mkdir -p "$builddir"
winpath() { cygpath -w "$1"; }
"$csc" -nologo -target:winexe -platform:x64 -optimize+ -warn:4 -warnaserror+ \
  "-out:$(winpath $builddir/DesktopTidy.exe)" "-win32manifest:$(winpath src/DesktopTidy/app.manifest)" \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Runtime.Serialization.dll \
  "-win32icon:$(winpath src/DesktopTidy/Assets/desktop-tidy.ico)" "-resource:$(winpath src/DesktopTidy/Assets/desktop-tidy.ico),DesktopTidy.AppIcon" "$(winpath src/DesktopTidy/UI/AppBrand.cs)" "$(winpath src/DesktopTidy/Program.cs)" "$(winpath src/DesktopTidy/TrayApplicationContext.cs)" \
  "$(winpath src/DesktopTidy/UI/MainForm.cs)" "$(winpath src/DesktopTidy/UI/DesktopListControl.cs)" "$(winpath src/DesktopTidy/UI/Theme.cs)" "$(winpath src/DesktopTidy/UI/WorkspaceChrome.cs)" "$(winpath src/DesktopTidy/UI/SurfaceDrawing.cs)" \
  "$(winpath src/DesktopTidy/Core/Classification.cs)" "$(winpath src/DesktopTidy/Core/LayoutStore.cs)" "$(winpath src/DesktopTidy/Core/WorkspaceController.cs)" "$(winpath src/DesktopTidy/UI/ClassificationBox.cs)" "$(winpath src/DesktopTidy/UI/WorkspaceForm.cs)" "$(winpath src/DesktopTidy/Windows/DesktopIconVisibility.cs)" "$(winpath src/DesktopTidy/Windows/DesktopAttachment.cs)" "$(winpath src/DesktopTidy/Windows/ShellIconCache.cs)" \
  "$(winpath src/DesktopTidy/Windows/DesktopReader.cs)" "$(winpath src/DesktopTidy/Windows/NativeMethods.cs)"
"$csc" -nologo -target:exe -platform:x64 -optimize+ -warn:4 -warnaserror+ \
  "-out:$(winpath $builddir/MemoryProbe.exe)" "-win32manifest:$(winpath src/DesktopTidy/app.manifest)" -r:System.dll -r:System.Core.dll -r:System.Runtime.Serialization.dll \
  "$(winpath tools/MemoryProbe/Program.cs)"
"$csc" -nologo -target:exe -platform:x64 -optimize+ -warn:4 -warnaserror+ -main:WorkspaceTests \
  "-out:$(winpath $builddir/WorkspaceTests.exe)" "-win32manifest:$(winpath src/DesktopTidy/app.manifest)" \
  -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Runtime.Serialization.dll \
  "$(winpath tools/WorkspaceTests/Program.cs)" \
  "-win32icon:$(winpath src/DesktopTidy/Assets/desktop-tidy.ico)" "-resource:$(winpath src/DesktopTidy/Assets/desktop-tidy.ico),DesktopTidy.AppIcon" "$(winpath src/DesktopTidy/UI/AppBrand.cs)" "$(winpath src/DesktopTidy/Program.cs)" "$(winpath src/DesktopTidy/TrayApplicationContext.cs)" "$(winpath src/DesktopTidy/Core/Classification.cs)" "$(winpath src/DesktopTidy/Core/LayoutStore.cs)" \
  "$(winpath src/DesktopTidy/Core/WorkspaceController.cs)" "$(winpath src/DesktopTidy/UI/ClassificationBox.cs)" "$(winpath src/DesktopTidy/UI/WorkspaceForm.cs)" "$(winpath src/DesktopTidy/Windows/DesktopIconVisibility.cs)" "$(winpath src/DesktopTidy/UI/DesktopListControl.cs)" "$(winpath src/DesktopTidy/UI/MainForm.cs)" \
  "$(winpath src/DesktopTidy/UI/WorkspaceChrome.cs)" "$(winpath src/DesktopTidy/UI/SurfaceDrawing.cs)" "$(winpath src/DesktopTidy/UI/Theme.cs)" "$(winpath src/DesktopTidy/Windows/DesktopAttachment.cs)" "$(winpath src/DesktopTidy/Windows/DesktopReader.cs)" \
  "$(winpath src/DesktopTidy/Windows/NativeMethods.cs)" "$(winpath src/DesktopTidy/Windows/ShellIconCache.cs)"
cp src/DesktopTidy/App.config $builddir/WorkspaceTests.exe.config
cp src/DesktopTidy/App.config $builddir/DesktopTidy.exe.config
cp src/DesktopTidy/App.config $builddir/MemoryProbe.exe.config
printf '%s\n' "Release x64 构建通过：$builddir/DesktopTidy.exe"
