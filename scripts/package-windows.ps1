param(
    [switch]$Check,
    [string]$WebView2Installer,
    [string]$InnoCompiler,
    [string]$VisualCppRuntimeDirectory,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.10.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$problems = [System.Collections.Generic.List[string]]::new()
if (!$VisualCppRuntimeDirectory) {
    $VisualCppRuntimeDirectory = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT" -Directory -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$VisualCppRuntimeDirectory -or !(Test-Path (Join-Path $VisualCppRuntimeDirectory 'vcruntime140.dll'))) { $problems.Add('Pass -VisualCppRuntimeDirectory with the Visual Studio x64 redistributable CRT folder for bundled offline OCR.') }
$sdks = & $dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or !($sdks -match '^10\.')) { $problems.Add('Install the .NET 10 SDK (the runtime alone is insufficient).') }
$workloads = & $dotnet workload list
if ($LASTEXITCODE -ne 0 -or !(($workloads -join "`n") -match '(?m)^maui(-windows)?\s')) { $problems.Add('Install the MAUI Windows workload using the .NET 10 SDK: dotnet workload install maui-windows') }
if (!$InnoCompiler) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler)) { $problems.Add('Install Inno Setup 6, or pass -InnoCompiler with the full path to ISCC.exe.') }
if (!$WebView2Installer -or !(Test-Path -LiteralPath $WebView2Installer)) {
    $problems.Add('Pass -WebView2Installer with the Microsoft Evergreen Standalone WebView2 x64 installer, for offline installation.')
} else {
    $WebView2Installer = (Resolve-Path -LiteralPath $WebView2Installer).Path
    $signature = Get-AuthenticodeSignature -LiteralPath $WebView2Installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { $problems.Add('The WebView2 installer must have a valid Microsoft Corporation signature.') }
    if ((Get-Item -LiteralPath $WebView2Installer).Length -lt 10MB) { $problems.Add('Use the full standalone WebView2 installer, not the small online bootstrapper.') }
}
# Optional code signing (0.26.0). Set SIERRA_SIGN_THUMBPRINT (a certificate in the current user's or machine's store)
# or SIERRA_SIGN_PFX with SIERRA_SIGN_PFX_PASSWORD. SIERRA_SIGN_TIMESTAMP overrides the RFC 3161 timestamp server.
$signing = [bool]($env:SIERRA_SIGN_THUMBPRINT -or $env:SIERRA_SIGN_PFX)
$signTool = $null
if ($signing) {
    $signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (!$signTool) { $problems.Add('Code signing was requested but signtool.exe was not found. Install the Windows SDK signing tools.') }
    if ($env:SIERRA_SIGN_PFX -and !(Test-Path -LiteralPath $env:SIERRA_SIGN_PFX)) { $problems.Add('SIERRA_SIGN_PFX does not point to a certificate file.') }
}
function Invoke-Signing([string[]]$Files) {
    if (!$signing) { return }
    $timestamp = if ($env:SIERRA_SIGN_TIMESTAMP) { $env:SIERRA_SIGN_TIMESTAMP } else { 'http://timestamp.digicert.com' }
    foreach ($file in $Files) {
        $arguments = @('sign', '/fd', 'SHA256', '/tr', $timestamp, '/td', 'SHA256', '/d', 'Sierra Business')
        if ($env:SIERRA_SIGN_THUMBPRINT) { $arguments += @('/sha1', $env:SIERRA_SIGN_THUMBPRINT) } else { $arguments += @('/f', $env:SIERRA_SIGN_PFX, '/p', $env:SIERRA_SIGN_PFX_PASSWORD) }
        & $signTool @arguments $file | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Code signing failed for $(Split-Path -Leaf $file)." }
        $result = Get-AuthenticodeSignature -LiteralPath $file
        if ($result.Status -ne 'Valid') { throw "Signature verification failed for $(Split-Path -Leaf $file): $($result.StatusMessage)" }
        Write-Output "Signed: $(Split-Path -Leaf $file) by $($result.SignerCertificate.Subject)"
    }
}
if ($problems.Count -gt 0) {
    Write-Output 'Windows packaging is blocked by these prerequisites:'
    $problems | ForEach-Object { Write-Output " - $_" }
    exit 1
}
if ($Check) { Write-Output 'Windows package prerequisites are present. The native build and clean-machine installation still require verification.'; exit 0 }

Push-Location $root
try {
    # Use a fresh unique staging folder, never clean or reuse a user-selected path.
    $staging = Join-Path $root ('artifacts\windows\staging-' + [Guid]::NewGuid().ToString('N'))
    $output = Join-Path $root ('artifacts\windows\' + $Version)
    New-Item -ItemType Directory -Force $staging,$output | Out-Null
    & $dotnet restore SierraBusiness.sln
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
    & $dotnet test tests\SierraBusiness.IntegrationTests -c Release --no-restore --blame-crash --blame-hang-timeout 5m --results-directory "artifacts\windows\$Version\tests" --logger "trx;LogFileName=tests.trx" --logger "console;verbosity=normal"
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed; no installer will be produced.' }
    & $dotnet run --project tests\SierraBusiness.WindowsCaptureSmoke -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Offline OCR smoke checks failed; no installer will be produced.' }
    & $dotnet publish src\SierraBusiness.Maui\SierraBusiness.Maui.csproj -c Release -f net10.0-windows10.0.19041.0 -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true --self-contained true -p:PublishTrimmed=false "-p:ApplicationDisplayVersion=$Version" "-p:Version=$Version" -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Native Windows publish failed; no installer will be produced.' }
    if (!(Test-Path -LiteralPath (Join-Path $staging 'SierraBusiness.Maui.exe'))) { throw 'Native application executable missing from publish output.' }
    Invoke-Signing @((Join-Path $staging 'SierraBusiness.Maui.exe'))
    foreach ($runtime in (Get-ChildItem -LiteralPath $VisualCppRuntimeDirectory -Filter '*.dll' -File)) {
        $signature = Get-AuthenticodeSignature -LiteralPath $runtime.FullName
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'Visual C++ runtime signature verification failed.' }
        Copy-Item -LiteralPath $runtime.FullName -Destination $staging
    }
    foreach ($required in @('wwwroot\print.js', 'wwwroot\capture.css', 'OcrData\eng.traineddata', 'x64\tesseract50.dll', 'x64\leptonica-1.82.0.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $staging $required))) { throw "Offline OCR dependency missing: $required" }
    }
    if ((Get-FileHash (Join-Path $staging 'OcrData\eng.traineddata') -Algorithm SHA256).Hash -ne '7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2') { throw 'English OCR model checksum mismatch.' }
    if (Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -match '\.db($|-)|credentials' }) { throw 'Refusing to package local databases or credentials.' }
    & $InnoCompiler "/DPublishDir=$staging" "/DOutputDir=$output" "/DWebView2Installer=$WebView2Installer" "/DAppVersion=$Version" (Join-Path $root 'packaging\windows\SierraBusiness.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $output 'SierraBusiness-Setup-x64.exe'
    if (!(Test-Path -LiteralPath $installer)) { throw 'Installer output not found.' }
    Invoke-Signing @($installer)
    if (!$signing) { Write-Output 'Unsigned build: set SIERRA_SIGN_THUMBPRINT or SIERRA_SIGN_PFX to sign the application and installer.' }
    $hash = Get-FileHash -LiteralPath $installer -Algorithm SHA256
    "$($hash.Hash)  SierraBusiness-Setup-x64.exe" | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt')
    Write-Output "Created: $installer"
    Write-Output 'Test install, launch, offline trade, restart, upgrade and uninstall on a clean Windows machine before distribution.'
} finally { Pop-Location }





