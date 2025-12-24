# build_installers.ps1
$ErrorActionPreference = "Stop"

function Find-ISCC {
    if (Get-Command iscc -ErrorAction SilentlyContinue) {
        return "iscc"
    }
    
    $commonPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 6\iscc.exe"
    )
    
    foreach ($path in $commonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    
    return $null
}

$ISCC = Find-ISCC
if (-not $ISCC) {
    Write-Error "Inno Setup Compiler (iscc.exe) not found. Please install Inno Setup 6+ and add it to PATH or install to default location."
}

Write-Host "Found Inno Setup: $ISCC" -ForegroundColor Green

# 1. Publish Slim versions
Write-Host "`n=== Publishing Slim Versions ===" -ForegroundColor Cyan
dotnet publish StickyNote.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=false -o publish-slim/win-x64
dotnet publish StickyNote.csproj -c Release -r win-x86 /p:PublishSingleFile=true /p:SelfContained=false -o publish-slim/win-x86

# 2. Publish Self-contained versions
Write-Host "`n=== Publishing Self-contained Versions ===" -ForegroundColor Cyan
dotnet publish StickyNote.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish-sc/win-x64
dotnet publish StickyNote.csproj -c Release -r win-x86 /p:PublishSingleFile=true /p:SelfContained=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish-sc/win-x86

# 3. Compile Installers
Write-Host "`n=== Compiling Installers ===" -ForegroundColor Cyan

$installers = @(
    @{ Script = "installer\StickyNote_slim.iss"; Arch = "x64" },
    @{ Script = "installer\StickyNote_slim.iss"; Arch = "x86" },
    @{ Script = "installer\StickyNote_sc.iss"; Arch = "x64" },
    @{ Script = "installer\StickyNote_sc.iss"; Arch = "x86" }
)

foreach ($inst in $installers) {
    $cmd = "& `"$ISCC`" `"$($inst.Script)`" /DArch=$($inst.Arch)"
    Write-Host "Building $($inst.Script) [$($inst.Arch)]..." -ForegroundColor Yellow
    Invoke-Expression $cmd
}

Write-Host "`nBuild Complete! Installers are in installer/out/" -ForegroundColor Green
