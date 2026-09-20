# NODR release checklist

## Build
- Run `build-release.bat` on Windows x64 with the .NET 10 SDK installed.
- For the installer, install Inno Setup 6 and run the script again.
- Expected artifacts are written to `artifacts/`:
  - `NODR-Portable-x64-v1.14.15.zip`
  - `NODR-Setup-x64-v1.14.15.exe`

## Smoke test before publishing
- Test on Windows 10/11 x64, preferably on a clean VM or a second PC.
- Installer: install, launch, relaunch, upgrade/reinstall, uninstall.
- Portable: extract to a new folder and launch `NODR.exe` directly.
- Verify Overview monitoring, Cleanup scan/clean, Files scan/delete-to-Recycle-Bin, and Uninstaller flow.
- Verify language defaults to English and theme defaults to System on a clean profile.
- Verify NODR does not show console/white transient windows during normal flows.
- Check Windows Defender/SmartScreen behavior.

## Distribution notes
- NODR is currently unsigned. Windows SmartScreen may warn users until a trusted code-signing reputation exists.
- Do not describe an unsigned build as trusted/signed.
- `LibreHardwareMonitorLib` is MPL-2.0 licensed. Include its required license/notices with public distribution and keep the corresponding source availability obligations in mind.
- NODR user settings/logs live under `%LOCALAPPDATA%\NODR`; uninstalling the app intentionally does not erase user settings by default.
