# Disposable verification only: never points at the normal development project's volumes.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$applicationRoot = Split-Path -Parent $PSScriptRoot
$projectName = 'jobposting-stage8-' + [Guid]::NewGuid().ToString('N')
$dockerEnvironment = @{}
# The verification profile comes only from the committed example, never ambient developer credentials.
foreach ($line in (Get-Content -LiteralPath (Join-Path $applicationRoot '.env.example'))) {
    if ($line -match '^([A-Z][A-Z0-9_]*)=(.*)$') { $dockerEnvironment[$Matches[1]] = $Matches[2] }
}
foreach ($portSetting in @('POSTGRES_PORT', 'RABBITMQ_PORT', 'RABBITMQ_MANAGEMENT_PORT', 'JOB_POSTING_API_PORT')) { $dockerEnvironment[$portSetting] = '0' }
$dockerEnvironment['COMPOSE_DISABLE_ENV_FILE'] = '1'

function Assert-Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Invoke-Docker([string[]]$arguments) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'docker'
    $start.WorkingDirectory = $applicationRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Native argument quoting works in Windows PowerShell 5.1 and PowerShell 7; no shell evaluates it.
    $start.Arguments = ($arguments | ForEach-Object {
        $escaped = [regex]::Replace($_, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    }) -join ' '
    foreach ($item in $dockerEnvironment.GetEnumerator()) { $start.EnvironmentVariables[$item.Key] = $item.Value }
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(300000)) { $process.Kill(); throw 'Docker verification command exceeded five minutes.' }
        $text = $output.GetAwaiter().GetResult()
        $failure = $errorOutput.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw ('Docker verification failed: ' + $failure) }
        return $text.Trim()
    } finally { $process.Dispose() }
}
function Invoke-Compose([string[]]$arguments) {
    Invoke-Docker (@('compose', '--project-name', $projectName, '--env-file', '.env.example') + $arguments)
}
function Read-Sql([string]$sql) {
    Invoke-Compose @('exec', '-T', 'postgres', 'psql', ('--username=' + $dockerEnvironment.POSTGRES_USER), ('--dbname=' + $dockerEnvironment.POSTGRES_DB), '--tuples-only', '--no-align', '--command', $sql)
}
function Send-Job([string]$key, [string]$payload) {
    $message = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, ($apiUrl + '/api/jobs'))
    $message.Headers.Add('Idempotency-Key', $key)
    $message.Content = New-Object System.Net.Http.StringContent($payload, [Text.Encoding]::UTF8, 'application/json')
    try {
        $response = $client.SendAsync($message).GetAwaiter().GetResult()
        try { return @{ Status = [int]$response.StatusCode; Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() } }
        finally { $response.Dispose() }
    } finally { $message.Dispose() }
}
function Read-QueueCount {
    $response = $management.GetAsync($managementUrl + '/api/queues/%2F/job-post-queue').GetAwaiter().GetResult()
    try { $response.EnsureSuccessStatusCode() | Out-Null; return ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json).messages }
    finally { $response.Dispose() }
}
function Wait-QueueCount([int]$expected) {
    # Management statistics are refreshed asynchronously, not at the confirmation boundary.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try { if ((Read-QueueCount) -eq $expected) { return } }
        catch { if ($watch.Elapsed.TotalSeconds -gt 20) { throw } }
        if ($watch.Elapsed.TotalSeconds -gt 20) { throw 'Queue count did not settle to the expected value.' }
        Start-Sleep -Milliseconds 250
    }
}
function Wait-Ready {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        try {
            $response = $client.GetAsync($apiUrl + '/health/ready').GetAwaiter().GetResult()
            try { if ([int]$response.StatusCode -eq 200) { return } } finally { $response.Dispose() }
        } catch { if ($watch.Elapsed.TotalSeconds -gt 90) { throw } }
        if ($watch.Elapsed.TotalSeconds -gt 90) { throw 'API readiness did not recover within 90 seconds.' }
        Start-Sleep -Milliseconds 500
    }
}
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(15)
$management = New-Object System.Net.Http.HttpClient
$management.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Basic', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($dockerEnvironment.RABBITMQ_DEFAULT_USER + ':' + $dockerEnvironment.RABBITMQ_DEFAULT_PASS)))
$success = $false
$ownsProject = $false
try {
    Write-Output 'Stage 8: validate and build the production Compose subset.'
    Invoke-Compose @('config', '--quiet') | Out-Null
    # Fail before startup if this supposedly new project's volume labels already exist.
    Assert-Check ((Invoke-Docker @('volume', 'ls', '--quiet', '--filter', ('label=com.docker.compose.project=' + $projectName))) -eq '') 'Disposable project already owns volumes.'
    $ownsProject = $true
    Invoke-Compose @('up', '--build', '--detach', '--wait', '--wait-timeout', '180') | Out-Null
    $apiUrl = 'http://' + (Invoke-Compose @('port', 'job-posting-api', '8080'))
    $managementUrl = 'http://' + (Invoke-Compose @('port', 'rabbitmq', '15672'))
    Wait-Ready
    Assert-Check ((Read-Sql 'SELECT count(*) FROM "__EFMigrationsHistory"') -eq '2') 'Expected migrations were not applied.'
    Assert-Check ((Read-Sql 'SELECT count(*) FROM jobs') -eq '0') 'Database did not start empty.'
    Assert-Check ((Invoke-Compose @('exec', '-T', 'job-posting-api', 'id', '-u')) -eq '1654') 'API is not running as the expected non-root user.'
    Assert-Check ((Invoke-Compose @('exec', '-T', 'job-posting-api', 'dotnet', '--list-sdks')) -eq '') 'SDK leaked into the runtime image.'
    Write-Output 'PASS: fresh volumes, sequenced migrations, readiness and non-root runtime.'

    $payload = @{ title = 'Engineer'; department = 'Engineering'; location = 'Toronto'; description = 'Build a small API'; salaryMin = 100; salaryMax = 200; closingDate = [DateTime]::UtcNow.AddDays(10).ToString('yyyy-MM-dd') } | ConvertTo-Json -Compress
    $created = Send-Job 'compose-create' $payload
    Assert-Check ($created.Status -eq 202) 'Container POST did not return 202.'
    $replay = Send-Job 'compose-create' $payload
    Assert-Check ($replay.Status -eq 202 -and $replay.Body -ceq $created.Body) 'Same-key replay did not retain the exact saved response.'
    $conflict = Send-Job 'compose-create' ($payload.Replace('Engineer', 'Other'))
    Assert-Check ($conflict.Status -eq 409 -and ($conflict.Body | ConvertFrom-Json).code -eq 'idempotency_key_conflict') 'Conflicting payload did not return 409.'
    Assert-Check ((Read-Sql 'SELECT count(*) FROM jobs WHERE published_at IS NOT NULL') -eq '1') 'Publication status was not persisted.'
    Wait-QueueCount 1
    $metadataSql = "SELECT concat_ws(':',id::text,event_id::text,published_at::text,idempotency_key_digest,request_fingerprint,canonicalization_version::text,md5(response_json)) FROM jobs"
    $originalMetadata = Read-Sql $metadataSql
    Write-Output 'PASS: container networking, confirmed POST, exact replay, conflict and one queued event.'

    Invoke-Compose @('restart', 'postgres', 'rabbitmq', 'job-posting-api') | Out-Null
    $apiUrl = 'http://' + (Invoke-Compose @('port', 'job-posting-api', '8080'))
    $managementUrl = 'http://' + (Invoke-Compose @('port', 'rabbitmq', '15672'))
    Wait-Ready
    Assert-Check ((Read-Sql $metadataSql) -ceq $originalMetadata) 'Restart changed persisted job metadata.'
    $replay = Send-Job 'compose-create' $payload
    Assert-Check ($replay.Status -eq 202 -and $replay.Body -ceq $created.Body) 'Replay after restart changed.'
    Wait-QueueCount 1
    Write-Output 'PASS: restart retains job/idempotency/publication metadata and queued data.'

    Invoke-Compose @('stop', 'rabbitmq') | Out-Null
    $outage = Send-Job 'compose-outage' $payload
    Assert-Check ($outage.Status -eq 503 -and ($outage.Body | ConvertFrom-Json).code -eq 'publication_failed') 'Broker outage did not produce safe compensated failure.'
    Assert-Check ((Read-Sql 'SELECT count(*) FROM jobs') -eq '1') 'Broker failure left an uncompensated new job.'
    $replay = Send-Job 'compose-create' $payload
    Assert-Check ($replay.Status -eq 202 -and $replay.Body -ceq $created.Body) 'Completed replay incorrectly depends on current broker availability.'
    Invoke-Compose @('start', 'rabbitmq') | Out-Null
    $managementUrl = 'http://' + (Invoke-Compose @('port', 'rabbitmq', '15672'))
    Wait-Ready
    Wait-QueueCount 1
    Write-Output 'PASS: broker outage returns 503, removes only the new row and does not republish completed work.'

    $stopping = [Diagnostics.Stopwatch]::StartNew()
    Invoke-Compose @('stop', 'job-posting-api') | Out-Null
    Assert-Check ($stopping.Elapsed.TotalSeconds -lt 30) 'API stop exceeded the host shutdown budget.'
    $apiContainer = Invoke-Compose @('ps', '--all', '--quiet', 'job-posting-api')
    Assert-Check ((Invoke-Docker @('inspect', '--format', '{{.State.ExitCode}}', $apiContainer)) -eq '0') 'Graceful API stop did not exit cleanly.'
    Invoke-Compose @('run', '--rm', '--no-deps', 'migrate') | Out-Null
    Assert-Check ((Read-Sql $metadataSql) -ceq $originalMetadata) 'Explicit migration rerun changed saved jobs.'
    Write-Output 'PASS: graceful stop and idempotent explicit migration rerun.'

    # This project is disposable, but even here recreation first preserves the named volumes.
    Invoke-Compose @('down') | Out-Null
    Invoke-Compose @('up', '--detach', '--wait', '--wait-timeout', '180') | Out-Null
    $apiUrl = 'http://' + (Invoke-Compose @('port', 'job-posting-api', '8080'))
    $managementUrl = 'http://' + (Invoke-Compose @('port', 'rabbitmq', '15672'))
    Wait-Ready
    Assert-Check ((Read-Sql $metadataSql) -ceq $originalMetadata) 'Container recreation changed saved metadata.'
    $replay = Send-Job 'compose-create' $payload
    Assert-Check ($replay.Status -eq 202 -and $replay.Body -ceq $created.Body) 'Recreated-container replay changed.'
    Wait-QueueCount 1
    Write-Output 'PASS: container recreation retains both named-volume databases and broker messages.'
    $success = $true
} finally {
    $client.Dispose(); $management.Dispose()
    # The generated project name is never accepted from input; delete only its newly owned test volumes.
    Assert-Check ($projectName -match '^jobposting-stage8-[0-9a-f]{32}$') 'Unsafe cleanup project name.'
    if ($ownsProject) {
        Invoke-Compose @('down', '--volumes', '--remove-orphans') | Out-Null
        Invoke-Docker @('image', 'rm', ($projectName + '-api:local'), ($projectName + '-migrations:local')) | Out-Null
    }
}
if ($success) { Write-Output 'PASS: Stage 8 disposable Compose verification complete; owned test resources removed.' }
