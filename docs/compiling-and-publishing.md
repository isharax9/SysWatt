# Compiling and Publishing Versions

This document details how versions are compiled, published, and organized in SysWatt.

---

## 1. Directory Structure and Version Tagging

All application publish outputs are organized inside the `artifacts/publish/` directory using semantic version tags (`v<Major>.<Minor>.<Patch>[-<Prerelease>]`).

```text
artifacts/
├── SHA256SUMS.txt
├── SysWatt-0.1.0-win-x64-portable.zip
├── SysWatt-0.2.0-win-x64-portable.zip
├── SysWatt-Setup-0.1.0.exe
├── SysWatt-Setup-0.2.0.exe
└── publish/
    ├── v0.1.0/
    │   ├── LICENSE
    │   ├── portable.flag
    │   ├── SysWatt.App.exe
    │   ├── SysWatt.App.pdb
    │   ├── SysWatt.Core.pdb
    │   ├── SysWatt.Infrastructure.pdb
    │   └── THIRD-PARTY-NOTICES.md
    └── v0.2.0/
        ├── LICENSE
        ├── portable.flag
        ├── SysWatt.App.exe
        ├── SysWatt.App.pdb
        ├── SysWatt.Core.pdb
        ├── SysWatt.Infrastructure.pdb
        ├── THIRD-PARTY-NOTICES.md
        └── data/
            └── energy-history.db
```

### Why Version-Tagged Folders?
1. **Prevents file clobbering**: Rebuilding a new version does not wipe or overwrite past compiled outputs.
2. **Local testing isolation**: Portable builds store user data (such as SQLite history in `data/energy-history.db`) alongside the executable. Version tagging isolates databases between versions.
3. **Consistency with Git & Releases**: The directory name `vX.Y.Z` directly mirrors Git tags (`v0.1.0`, `v0.2.0`) and GitHub Release tags.

---

## 2. Compiling and Packaging Automatically (Recommended)

The packaging script at [`scripts/package.ps1`](../scripts/package.ps1) automates the entire process: publishing the single-file binary, copying licenses, archiving the portable zip, compiling the Inno Setup installer, and computing SHA-256 checksums.

### Usage

Run PowerShell from the repository root:

```powershell
# Using semantic version without 'v'
./scripts/package.ps1 -Version 0.2.0

# Or with 'v' prefix
./scripts/package.ps1 -Version v0.2.0

# Prerelease versions are also supported
./scripts/package.ps1 -Version 0.3.0-preview.1
```

### What `package.ps1` Does:
1. **Validates & Normalizes Version**: Cleans the input (e.g. `v0.2.0` becomes version `0.2.0` and tag `v0.2.0`).
2. **Prepares Version Directory**: Creates `artifacts/publish/v<Version>/` (clearing only that specific version directory if it already exists, keeping other version directories intact).
3. **Executes `dotnet publish`**:
   - Single-file self-contained executable for `win-x64`.
   - Embeds the specified version into the binary assembly metadata.
   - Outputs directly into `artifacts/publish/v<Version>/`.
4. **Copies Companion Files**: Adds `portable.flag`, `LICENSE`, and `THIRD-PARTY-NOTICES.md`.
5. **Creates Portable Archive**: Packages `artifacts/SysWatt-<Version>-win-x64-portable.zip`.
6. **Compiles Installer**: Invokes Inno Setup (`ISCC.exe`) targeting the specific version directory and outputs `artifacts/SysWatt-Setup-<Version>.exe`.
7. **Updates SHA256 Checksums**: Recalculates `artifacts/SHA256SUMS.txt` for all release zips and installers.

---

## 3. Compiling Manually via .NET CLI

If you want to compile directly into a versioned publish directory without creating installers or zip archives, use `dotnet publish`:

```powershell
# Define target version and folder tag
$Version = "0.2.0"
$PublishDir = "artifacts\publish\v$Version"

# 1. Restore, build, and test (standard prerequisites)
dotnet restore SysWatt.sln --configfile NuGet.Config
dotnet build SysWatt.sln --no-restore --configuration Release
dotnet test SysWatt.sln --no-build --configuration Release

# 2. Publish single-file binary into version-tagged folder
dotnet publish src\SysWatt.App\SysWatt.App.csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    --output $PublishDir

# 3. Add companion files for portable mode
New-Item -ItemType File -Path "$PublishDir\portable.flag" -Force
Copy-Item LICENSE, THIRD-PARTY-NOTICES.md -Destination $PublishDir
```

---

## 4. Compiling the Inno Setup Installer Manually

When you need to compile the installer separately pointing to an existing versioned folder:

```powershell
$Version = "0.2.0"
$PublishDir = (Resolve-Path "artifacts\publish\v$Version").Path

# Locate ISCC.exe (Inno Setup Compiler)
$iscc = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

# Compile installer
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$PublishDir" installer\SysWatt.iss
```

The installer will be generated at `artifacts\SysWatt-Setup-<Version>.exe`.

---

## 5. Verification & Smoke Testing

After compiling a version into its folder, verify its functionality and embedded version tag:

### Check Embedded Assembly Version
```powershell
(Get-Item "artifacts\publish\v0.2.0\SysWatt.App.exe").VersionInfo | Format-List ProductVersion, FileVersion, ProductName
```

### Run Lifecycle Smoke Test
SysWatt includes a headless non-interactive smoke test that tests sensor initialization, telemetry collection, and clean shutdown for 2.5 seconds:

```powershell
.\artifacts\publish\v0.2.0\SysWatt.App.exe --smoke-test
```
Exit code `0` indicates success.

---

## 6. GitHub Actions CI/CD Release Workflow

The automated GitHub release workflow ([`.github/workflows/release.yml`](../.github/workflows/release.yml)) is triggered when pushing git tags formatted as `v*.*.*`:

1. Extracts the version number (`${{ github.ref_name }}` without `v`).
2. Runs `./scripts/package.ps1 -Version <version>`.
3. Packages into `artifacts/publish/v<version>`.
4. Uploads zip, installer, and checksums to the corresponding GitHub Release.
