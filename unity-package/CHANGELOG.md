# Changelog

## [0.1.5] - 2026-05-09

### Fixed
- Console double-click now correctly jumps to the error line in Zed. Unity can pass a relative path (e.g. `Assets/Scripts/Foo.cs`) for compile-error clicks; this is now resolved to an absolute path before being sent to Zed.
- CLI argument quoting updated to always wrap paths in double-quotes, preventing issues with special characters in paths.

## [0.1.3] - 2026-03-02

### Added
- Bundle `Microsoft.Unity.Analyzers` (v1.26.0, MIT) in `Analyzers~/`
- Generated `.csproj` files now include `<Analyzer>` reference to suppress false-positive IDE0051 on Unity message methods (Start, Update, Awake, etc.)

## [0.1.2] - 2026-03-01

### Changed
- Minimum Unity version lowered from 6000.3 to 2020.3
- Removed confirmation dialog after "Regenerate .csproj / .sln"

### Fixed
- Git URL in documentation corrected to `seless-yuu/ZedUnity`

## [0.1.1] - 2026-03-01

### Fixed
- Restored `IExternalCodeEditor` implementation (Zed now appears in External Tools dropdown)
- `AssembliesType.PlayerWithEditor` → `AssembliesType.Editor` (removed in Unity 6)
- Resolved `Debug` ambiguity between `System.Diagnostics.Debug` and `UnityEngine.Debug`
- `MakeRelativePath`: resolve relative source file paths before `Uri` conversion to avoid `UriFormatException`

## [0.1.0] - 2026-02-27

### Added
- Initial release
- `ZedCodeEditor` – `IExternalCodeEditor` implementation registering Zed as Unity's external script editor
- `ZedDiscovery` – automatic Zed installation detection for Windows, macOS, Linux
- `ProjectGeneration` – generates `.csproj` and `.sln` files from `CompilationPipeline` assemblies
- Auto-generates `omnisharp.json` and `.zed/settings.json` on project sync
- Preferences GUI panel with "Regenerate .csproj / .sln" button
- File open with line/column support: `zed path:line:column`
