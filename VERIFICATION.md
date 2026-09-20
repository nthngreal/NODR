# NODR v1.14.1 verification

- Fixed the v1.14.0 C# compile regression in `Services/UninstallerService.cs` line 487 by using an interpolated verbatim string for the registry display path.
- Residual Scan behavior is otherwise unchanged.
- XAML/XML parsing: PASS.
- UA/EN/RU localization JSON parsing: PASS.
- `bin/` and `obj/` removed from the release package.
- AssemblyVersion remains stable at 1.0.0.0; product/file version is 1.14.1.
- Windows .NET/WPF compilation/runtime: OPEN (not available in this environment).
