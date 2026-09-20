# NODR

**Cleaner. Faster. Freer.**

NODR is a focused Windows utility for monitoring your PC, reclaiming disposable space, finding large files, and uninstalling applications without pretending to be a “magic optimizer.”

> Built for Windows. Local-first. Conservative by design.

<p align="center">
  <img src="assets/screenshots/overview-dark.png" alt="NODR Overview in dark theme" width="860">
</p>

## What NODR does

### Overview

See CPU, GPU, RAM and storage information at a glance, check cleanup status, review system specs, and launch common maintenance tools.

### Cleanup

Scan disposable or recoverable data and choose exactly which categories to clean. Personal files are not included in cleanup scans.

<p align="center">
  <img src="assets/screenshots/cleanup-dark.png" alt="NODR Cleanup" width="860">
</p>

### Large files

Find files **100 MB or larger**, understand what is taking space, and decide what to remove. NODR does not automatically delete files found by the scanner.

<p align="center">
  <img src="assets/screenshots/files-dark.png" alt="NODR large file finder" width="860">
</p>

### Uninstaller

Review installed applications, open their registered uninstallers, and clean only narrowly attributable leftovers after uninstall.

<p align="center">
  <img src="assets/screenshots/uninstaller-dark.png" alt="NODR Uninstaller" width="860">
</p>

## Safety first

NODR deliberately avoids aggressive “optimizer” behavior.

- No registry cleaner
- No RAM cleaner
- No Prefetch cleaner
- No automatic deletion of personal files
- Large-file scans do not delete anything automatically
- User-file removal uses the Windows Recycle Bin where applicable
- Reparse points, junctions and symlinks are not followed during file scans
- Cleanup categories explain what is being removed

## Light & dark

NODR supports **System, Dark and Light** themes.

<p align="center">
  <img src="assets/screenshots/overview-light.png" alt="NODR Overview in light theme" width="860">
</p>

The interface is available in **English, Ukrainian and Russian**. English is the default language.

## Download

Public Windows builds are distributed in two forms:

- **Setup** — branded Windows installer
- **Portable** — self-contained ZIP; extract it and run `NODR.exe`

Downloads are published in the repository's **Releases** section.

## Requirements

- Windows 10/11 x64
- Public builds are self-contained
- .NET 10 SDK is required only when building from source

## Build from source

Run:

```bat
run.bat
```

To create release packages:

```bat
build-release.bat
```

The release builder publishes a self-contained Windows x64 build and creates the portable ZIP. With Inno Setup 6.6+ installed, it also builds the branded Setup executable.

## Privacy

NODR works locally. File and residual classification use deterministic local rules and do not depend on a cloud service.

## Third-party software

NODR uses `LibreHardwareMonitorLib` for hardware sensor access. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for redistribution notices and upstream licensing information.

## Current release

**v1.14.15** — tested on Windows as both the portable build and installed build.

---

Made with a preference for useful tools over feature bloat.
