param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.31.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Join-Path $root "artifacts/windows/$Version"
$snapshot = Join-Path $root "artifacts/windows/source-$Version"
$installer = Join-Path $release 'RokelaBusinessAdmin-Setup-x64.exe'
$checks = [Collections.Generic.List[object]]::new()
function Record([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([pscustomobject]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (!$Passed) { throw "$Name failed: $Detail" }
}
$actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
$checksum = (Get-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Raw).Trim()
Record 'Installer checksum' ($checksum -eq "$actualHash  RokelaBusinessAdmin-Setup-x64.exe") $actualHash
$info = (Get-Item -LiteralPath $installer).VersionInfo
Record 'Installer version' ($info.ProductVersion.Trim() -eq $Version) $info.ProductVersion
Record 'Installer product name' ($info.ProductName.Trim() -eq 'Rokela Business Admin') $info.ProductName
$staging = @(Get-ChildItem -LiteralPath (Join-Path $snapshot 'artifacts/windows') -Directory -Filter 'staging-*')
Record 'Unique immutable publish folder' ($staging.Count -eq 1) "Count: $($staging.Count)"
$publish = $staging[0].FullName
$exe = Join-Path $publish 'SierraBusiness.Maui.exe'
Record 'Published application version' ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0] -eq $Version) ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion)
$required = @('SierraBusiness.Maui.dll', 'coreclr.dll', 'hostfxr.dll', 'wwwroot/branding/rokela-logo.png', 'wwwroot/branding/rokela-icon.png', 'wwwroot/print.js', 'wwwroot/forms.js', 'wwwroot/capture.css', 'OcrData/eng.traineddata', 'x64/tesseract50.dll', 'x64/leptonica-1.82.0.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll')
foreach ($file in $required) { Record "Bundled $file" (Test-Path -LiteralPath (Join-Path $publish $file) -PathType Leaf) 'Required offline asset' }
foreach ($file in @('print.js', 'forms.js', 'app.css')) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $snapshot "src/SierraBusiness.Web/wwwroot/$file") -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash -LiteralPath (Join-Path $publish "wwwroot/$file") -Algorithm SHA256).Hash
    Record "Published $file matches source" ($sourceHash -eq $publishedHash) $publishedHash
}
$model = (Get-FileHash -LiteralPath (Join-Path $publish 'OcrData/eng.traineddata') -Algorithm SHA256).Hash
Record 'Offline OCR model checksum' ($model -eq '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2') $model
$privateFiles = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Name -match '\.db($|-)|credentials' })
Record 'No company databases or credential files' ($privateFiles.Count -eq 0) "Count: $($privateFiles.Count)"
$manifest = Get-Content -LiteralPath (Join-Path $snapshot 'source-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $file = [IO.Path]::GetFullPath((Join-Path $snapshot $entry.path))
    if (!$file.StartsWith($snapshot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Manifest path escaped the snapshot.' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Snapshot changed: $($entry.path)" }
}
Record 'Immutable source manifest' $true "$($manifest.Count) files checked"
[xml]$trx = Get-Content -LiteralPath (Join-Path $release 'tests/tests.trx')
$counters = $trx.TestRun.ResultSummary.Counters
Record 'Release tests passed' ($trx.TestRun.ResultSummary.outcome -eq 'Completed' -and [int]$counters.failed -eq 0 -and [int]$counters.error -eq 0 -and [int]$counters.passed -gt 0) "Passed $($counters.passed); total $($counters.total)"
$signature = (Get-AuthenticodeSignature -LiteralPath $installer).Status.ToString()
$report = [pscustomobject]@{
    version = $Version
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
    installer = $installer
    sha256 = $actualHash
    signature = $signature
    checks = $checks
    nativeAcceptance = 'Pending: installation, UI clicks, restart, physical printing and clean-machine upgrade/uninstall were not executed by this script.'
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'release-verification.json') -Encoding utf8
Write-Output "PASS: $($checks.Count) release artifact checks. Signature: $signature. Native acceptance remains pending."
