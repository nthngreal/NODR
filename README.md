# NODR

NODR is a focused Windows utility for system monitoring, safe cleanup, large-file discovery, and controlled application uninstall workflows.

## Requirements

- Windows 10/11 x64
- .NET 10 SDK is required only to build from source. Public release builds are self-contained.

## Build and run from source

Run `run.bat` on Windows with the .NET 10 SDK installed.

## Create release packages

Run `build-release.bat` on Windows. It publishes a self-contained x64 build and creates a portable ZIP. If Inno Setup 6 is installed, it also creates the Windows installer.

See `RELEASE_CHECKLIST.md` before publishing.

## Privacy

NODR works locally. It contains no AI integration, bundled AI model, or cloud AI dependency. File and residual classification uses deterministic local rules.

## Third-party software

NODR uses `LibreHardwareMonitorLib` for hardware sensor access. See `THIRD_PARTY_NOTICES.md` and the upstream licensing information before redistribution.
