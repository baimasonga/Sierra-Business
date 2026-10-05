# Uses the framework builder so this also works in Windows PowerShell 5.1,
# before the .NET 10 application and Npgsql assemblies have been loaded.
function New-SierraPostgresConnectionString {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseHost,
        [Parameter(Mandatory = $true)][string]$Username,
        [Parameter(Mandatory = $true)][string]$Password
    )
    $builder = New-Object System.Data.Common.DbConnectionStringBuilder
    $builder['Host'] = $DatabaseHost
    $builder['Port'] = 5432
    $builder['Database'] = 'postgres'
    $builder['Username'] = $Username
    $builder['Password'] = $Password
    $builder['SSL Mode'] = 'Require'
    $builder['Timeout'] = 30
    $builder['Command Timeout'] = 120
    $builder['Keepalive'] = 30
    return $builder.ConnectionString
}
