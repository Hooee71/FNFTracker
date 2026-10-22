# Builds the Microsoft Store package (FNFTracker-<version>.msix) to upload in Partner Center.
#
#   pwsh store\build-msix.ps1 -Version 0.6.0
#
# The package is unsigned on purpose: the Store signs it after certification.
# -Test builds with stand-in identity values (for CI checks) when store\identity.json isn't filled in yet;
# that package can't be uploaded to the Store.
param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $Test,
    [switch] $UseNuGetTools   # skip any installed Windows SDK (CI uses this to test the download path)
)
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got $Version)" }
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "out\store"
$app = Join-Path $out "app"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

# Identity from Partner Center.
$id = Get-Content (Join-Path $PSScriptRoot "identity.json") -Raw | ConvertFrom-Json
$unfilled = @($id.IdentityName, $id.Publisher, $id.PublisherDisplayName) | Where-Object { $_ -like "PASTE*" }
if ($unfilled) {
    if (-not $Test) { throw "Fill in store\identity.json from Partner Center (Product identity page) first." }
    $id.IdentityName = "Test.FNFArenaTracker"
    $id.Publisher = "CN=Test"
    $id.PublisherDisplayName = "Test"
    Write-Warning "identity.json isn't filled in: building a TEST package that the Store will reject."
}

# 1. Publish the app as a normal folder (not single-file: MSIX is already one file, and this starts faster).
dotnet publish (Join-Path $root "FnfTracker.App\FnfTracker.App.csproj") -c Release -r win-x64 `
    --self-contained true -p:StoreBuild=true -p:Version=$Version -o $app
if ($LASTEXITCODE) { throw "dotnet publish failed" }

# 2. Logos and manifest. The Store requires the 4th version number to be 0.
Copy-Item (Join-Path $PSScriptRoot "Images") (Join-Path $app "Images") -Recurse
$manifest = Get-Content (Join-Path $PSScriptRoot "AppxManifest.xml") -Raw
$values = @{
    IdentityName = $id.IdentityName; Publisher = $id.Publisher
    PublisherDisplayName = $id.PublisherDisplayName; DisplayName = $id.DisplayName; Version = "$Version.0"
}
foreach ($k in $values.Keys) { $manifest = $manifest.Replace("`$($k)", [Security.SecurityElement]::Escape($values[$k])) }
if ($manifest -match '\$\(\w+\)') { throw "Unfilled value in AppxManifest.xml: $($Matches[0])" }
Set-Content (Join-Path $app "AppxManifest.xml") $manifest -Encoding utf8

# 3. Pack with MakeAppx: from an installed Windows SDK if there is one, otherwise from Microsoft's
#    Windows SDK build-tools NuGet package (downloaded once into out\tools, no install needed).
function Find-MakeAppx([string] $folder) {
    if (-not (Test-Path $folder)) { return $null }
    Get-ChildItem $folder -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq "x64" } |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]"0.0" } } |
        Select-Object -Last 1 -ExpandProperty FullName
}
$makeappx = if (-not $UseNuGetTools) { Find-MakeAppx "${env:ProgramFiles(x86)}\Windows Kits\10\bin" }
if (-not $makeappx) {
    $tools = Join-Path $root "out\tools\sdk-buildtools"
    $makeappx = Find-MakeAppx $tools
    if (-not $makeappx) {
        Write-Host "Windows SDK not installed: downloading MakeAppx from NuGet (Microsoft.Windows.SDK.BuildTools)..."
        $feed = "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.buildtools"
        $latest = (Invoke-RestMethod "$feed/index.json").versions | Where-Object { $_ -notmatch "-" } | Select-Object -Last 1
        $zip = Join-Path $root "out\tools\sdk-buildtools.zip"
        New-Item (Split-Path $zip) -ItemType Directory -Force | Out-Null
        Invoke-WebRequest "$feed/$latest/microsoft.windows.sdk.buildtools.$latest.nupkg" -OutFile $zip
        Expand-Archive $zip $tools -Force
        Remove-Item $zip
        $makeappx = Find-MakeAppx $tools
    }
}
if (-not $makeappx) { throw "Couldn't find or download makeappx.exe." }
$msix = Join-Path $out ("FNFTracker-$Version" + $(if ($Test) { "-TEST" } else { "" }) + ".msix")
& $makeappx pack /d $app /p $msix /o
if ($LASTEXITCODE) { throw "makeappx failed" }

Write-Host "Built $msix"
if ($env:GITHUB_OUTPUT) { "msix=$msix" >> $env:GITHUB_OUTPUT }
