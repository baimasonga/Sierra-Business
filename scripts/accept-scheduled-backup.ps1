param([int]$TimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$worker = (Resolve-Path (Join-Path $root 'src/SierraBusiness.BackupWorker/bin/Release/net10.0/SierraBusiness.BackupWorker.exe')).Path
$web = (Resolve-Path (Join-Path $root 'src/SierraBusiness.Web/bin/Release/net10.0/SierraBusiness.Web.dll')).Path
$company = Join-Path $root ('.tools/backup task acceptance ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $company | Out-Null
$company = (Resolve-Path -LiteralPath $company).Path
$previous = @{}
foreach ($key in 'ConnectionStrings__Business','Database__Provider','Sync__Role','Bootstrap__Email','Bootstrap__Password') { $previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process') }
$registered = $null
try {
    $env:ConnectionStrings__Business = 'Data Source=' + (Join-Path $company 'sierra.db')
    $env:Database__Provider = 'Sqlite'
    $env:Sync__Role = 'Off'
    & dotnet $web --migrate *> (Join-Path $company 'migration.log')
    if ($LASTEXITCODE -ne 0) { throw "Fixture migration failed: $company" }
    $env:Bootstrap__Email = 'backup-acceptance@example.test'
    $env:Bootstrap__Password = 'Acceptance-' + [guid]::NewGuid().ToString('N') + '!9a'
    & dotnet $web --create-owner *> (Join-Path $company 'owner.log')
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture owner.' }
    $result = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'install-backup-task.ps1') -WorkerExecutable $worker -DataDirectory $company
    if ($LASTEXITCODE -ne 0 -or "$result" -notmatch '^Registered: (Rokela company backup [0-9A-F]{16})$') { throw 'Task registration failed.' }
    $registered = $Matches[1]
    $task = Get-ScheduledTask -TaskName $registered
    if ($task.Principal.RunLevel -ne 'Limited' -or $task.Actions.Execute -ne $worker -or $task.Actions.Arguments -ne ('--data-directory "' + $company + '"')) { throw 'Unexpected task principal or action.' }
    Start-ScheduledTask -TaskName $registered
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 500
        $manifest = Get-ChildItem -Path (Join-Path $company 'Backups/*.manifest.json') -ErrorAction SilentlyContinue | Select-Object -First 1
        $info = Get-ScheduledTaskInfo -TaskName $registered
        $state = (Get-ScheduledTask -TaskName $registered).State
    } until (($manifest -and $state -eq 'Ready') -or (Get-Date) -ge $deadline)
    if (!$manifest -or $state -ne 'Ready' -or $info.LastTaskResult -ne 0) { throw "Scheduled worker did not complete successfully. State: $state; result: $($info.LastTaskResult); fixture: $company" }
    $meta = Get-Content -LiteralPath $manifest.FullName -Raw | ConvertFrom-Json
    $file = Get-Item -LiteralPath ($manifest.FullName.Substring(0,$manifest.FullName.Length-'.manifest.json'.Length))
    if ($file.Length -le 0 -or $file.Length -ne $meta.Size -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $meta.Sha256) { throw 'Scheduled backup did not match the manifest.' }
    Write-Output "PASS: limited-user scheduled backup, exit code 0, verified size and SHA256. Evidence: $company"
} finally {
    if ($registered) { Unregister-ScheduledTask -TaskName $registered -Confirm:$false }
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
