param(
    [Parameter(Mandatory)][string]$WorkerExecutable,
    [Parameter(Mandatory)][string]$DataDirectory,
    [ValidateRange(1,168)][int]$IntervalHours = 1
)
$ErrorActionPreference = 'Stop'
$workerPath = (Resolve-Path -LiteralPath $WorkerExecutable).Path
$companyPath = (Resolve-Path -LiteralPath $DataDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $companyPath 'sierra.db'))) { throw 'Select an existing company data directory.' }
if ([IO.Path]::GetFileName($workerPath) -ne 'SierraBusiness.BackupWorker.exe') { throw 'Select the published SierraBusiness.BackupWorker.exe.' }
if ($companyPath.Contains('"')) { throw 'The company path cannot contain quotation marks.' }
$pathHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($companyPath.ToUpperInvariant()))).Substring(0,16)
$taskName = 'Rokela company backup ' + $pathHash
$taskAction = New-ScheduledTaskAction -Execute $workerPath -Argument ('--data-directory "' + $companyPath + '"')
$taskTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) -RepetitionInterval (New-TimeSpan -Hours $IntervalHours)
$windowsIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $windowsIdentity -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $taskTrigger -Principal $taskPrincipal -Settings $taskSettings -Description 'Verified backups while the desktop is closed; follows company automatic-backup and copy-folder settings. Runs while this Windows user is signed in.' -Force | Out-Null
Write-Output ('Registered: ' + $taskName)
