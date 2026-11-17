<#
.SYNOPSIS
  Builds the Microsoft Store package (MSIX) for Wydscop.

.DESCRIPTION
  1. Publishes Wydscop self-contained for x64 (no .NET install needed on the user's PC).
  2. Adds the package manifest (packaging/Package.appxmanifest) and the tile images (packaging/Images).
  3. Indexes the images with makepri so Windows picks the right size for each scale.
  4. Packs everything into artifacts/msix/Wydscop_<version>_x64.msix with makeappx.

  For the Store, pass the three identity values from Partner Center > your app > Product management >
  Product identity, and upload the .msix unsigned (the Store signs it). Without them the package gets
  test values that are only good for trying it on this PC.

  Needs the Windows 10/11 SDK (makeappx.exe, makepri.exe, signtool.exe) and the .NET 10 SDK.

.EXAMPLE
  # Store build
  .\packaging\build-msix.ps1 -IdentityName "12345YourName.Wydscop" -Publisher "CN=1A2B3C4D-..." -PublisherDisplayName "Your Name"

.EXAMPLE
  # Try it on this PC without signing (needs Developer Mode): installs from the unpacked folder
  .\packaging\build-msix.ps1 -Register
#>
[CmdletBinding()]
param(
    # Package/Identity/Name from Partner Center.
    [string]$IdentityName = "Wydscop.Dev",
    # Package/Identity/Publisher from Partner Center (starts with CN=).
    [string]$Publisher = "CN=Wydscop Dev",
    # Package/Properties/PublisherDisplayName from Partner Center.
    [string]$PublisherDisplayName = "Wydscop",
    # Sign the .msix with this certificate (thumbprint in Cert:\CurrentUser\My). Only for sideloading;
    # the certificate's subject must equal -Publisher. Not needed for the Store.
    [string]$CertificateThumbprint,
    # After building, install the unpacked folder for the current user (needs Developer Mode).
    [switch]$Register
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\msix"
$layout = Join-Path $out "layout"
$priWork = Join-Path $out "pri"

function Find-SdkTool([string]$name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tool = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows SDK (it comes with Visual Studio, or from https://developer.microsoft.com/windows/downloads/windows-sdk/)." }
    return $tool
}

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path -Leaf $exe) failed with exit code $LASTEXITCODE" }
}

$makeappx = Find-SdkTool "makeappx.exe"
$makepri = Find-SdkTool "makepri.exe"

# Version: <Version> from the csproj, plus ".0" (the Store requires the last part to be 0).
$csproj = Join-Path $root "src\Wydscop\Wydscop.csproj"
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $csproj" }
$packageVersion = "$version.0"
Write-Host "Wydscop $packageVersion as $IdentityName ($Publisher)"

# 1. Publish
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
dotnet publish $csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o $layout | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Get-ChildItem $layout -Filter *.pdb | Remove-Item

# 2. Manifest and images
$manifest = Get-Content (Join-Path $PSScriptRoot "Package.appxmanifest") -Raw
$values = @{
    'IdentityName'         = $IdentityName
    'Publisher'            = $Publisher
    'PublisherDisplayName' = $PublisherDisplayName
    'Version'              = $packageVersion
}
foreach ($key in $values.Keys) {
    $manifest = $manifest.Replace("`$$key`$", [Security.SecurityElement]::Escape($values[$key]))
}
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest, $utf8)
Copy-Item (Join-Path $PSScriptRoot "Images") (Join-Path $layout "Images") -Recurse

# 3. resources.pri: index only the manifest and images (not the hundreds of runtime files)
New-Item -ItemType Directory -Force $priWork | Out-Null
Copy-Item (Join-Path $layout "AppxManifest.xml") $priWork
Copy-Item (Join-Path $layout "Images") (Join-Path $priWork "Images") -Recurse
$priConfig = Join-Path $out "priconfig.xml"
Invoke-Tool $makepri @("createconfig", "/cf", $priConfig, "/dq", "en-US", "/pv", "10.0.0", "/o")
# The default config splits scales into separate .pri files meant for bundles; keep one resources.pri.
$config = [xml](Get-Content $priConfig -Raw)
$packaging = $config.SelectSingleNode("//packaging")
if ($packaging) { [void]$packaging.ParentNode.RemoveChild($packaging) }
$config.Save($priConfig)
Invoke-Tool $makepri @("new", "/pr", $priWork, "/cf", $priConfig, "/mn", (Join-Path $priWork "AppxManifest.xml"), "/of", (Join-Path $layout "resources.pri"), "/o")

# 4. Pack
$msix = Join-Path $out "Wydscop_${packageVersion}_x64.msix"
Invoke-Tool $makeappx @("pack", "/d", $layout, "/p", $msix, "/o")

if ($CertificateThumbprint) {
    $signtool = Find-SdkTool "signtool.exe"
    Invoke-Tool $signtool @("sign", "/fd", "SHA256", "/sha1", $CertificateThumbprint, $msix)
}

Write-Host ""
Write-Host "Package: $msix"

if ($Register) {
    # A packaged copy and a plain copy can't run at once (single-instance mutex), so stop the plain one.
    Get-Process Wydscop -ErrorAction SilentlyContinue | Stop-Process
    Add-AppxPackage -Register (Join-Path $layout "AppxManifest.xml") -ForceApplicationShutdown
    Write-Host "Installed for this user from $layout. Start it from the Start menu (Wydscop)."
    Write-Host "Remove it with: Get-AppxPackage $IdentityName | Remove-AppxPackage"
}
