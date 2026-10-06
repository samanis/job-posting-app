$ErrorActionPreference = 'Stop'
$dockerExecutable = (Get-Command docker -CommandType Application | Select-Object -First 1).Source
$applicationRoot = Split-Path $PSScriptRoot -Parent
$verificationProject = 'jobsearch-verify-' + [Guid]::NewGuid().ToString('N')
$verificationNetwork = $verificationProject + '-broker'
$verificationEnv = Join-Path $applicationRoot ('.env.' + $verificationProject)
function FreePort { $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop();return $port }
function Docker { param([string[]]$Arguments); & $dockerExecutable @Arguments;if($LASTEXITCODE -ne 0){throw 'Docker verification command failed.'} }
function Compose { param([string[]]$Arguments);Docker -Arguments (@('compose','--project-name',$verificationProject,'--env-file',$verificationEnv,'-f',(Join-Path $applicationRoot 'docker-compose.yml'),'-f',(Join-Path $applicationRoot 'compose.verify.yml'))+$Arguments) }
function WaitHttp { param([string]$Path);$deadline=[DateTime]::UtcNow.AddSeconds(90);while([DateTime]::UtcNow -lt $deadline){try{$result=Invoke-RestMethod -Uri ($apiUrl+$Path) -TimeoutSec 5;return $result}catch{Start-Sleep -Milliseconds 500}};throw 'HTTP verification did not become ready.' }
$apiPort=FreePort;$pgPort=FreePort;$brokerPort=FreePort;$apiUrl='http://127.0.0.1:'+$apiPort
$keyBytes=New-Object byte[] 32;$random=[Security.Cryptography.RandomNumberGenerator]::Create();$random.GetBytes($keyBytes);$random.Dispose()
$password=[Guid]::NewGuid().ToString('N');$key=[Convert]::ToBase64String($keyBytes)
$variables=@{SEARCH_POSTGRES_PASSWORD=$password;CURSOR_SIGNING_KEY=$key;SEARCH_POSTGRES_PORT=$pgPort;SEARCH_API_PORT=$apiPort;VERIFY_BROKER_PORT=$brokerPort;RABBITMQ_NETWORK=$verificationNetwork;RABBITMQ_HOST='verify-broker';RABBITMQ_USER='jobsearchverify';RABBITMQ_PASSWORD=$password;RABBITMQ_QUEUE='verify-'+$verificationProject;RABBITMQ_EXCHANGE='verify-'+$verificationProject}
$saved=@{};foreach($name in $variables.Keys){$saved[$name]=[Environment]::GetEnvironmentVariable($name);[Environment]::SetEnvironmentVariable($name,[string]$variables[$name])}
$networkCreated=$false
try {
 [IO.File]::WriteAllLines($verificationEnv,@($variables.Keys|ForEach-Object{$_+'='+$variables[$_]}),[Text.UTF8Encoding]::new($false))
 Docker -Arguments @('network','create','--label',('jobsearch.verifier='+$verificationProject),$verificationNetwork)|Out-Null;$networkCreated=$true
 Compose -Arguments @('config','--quiet')
 Compose -Arguments @('build','migrate','job-search-api')
 Compose -Arguments @('up','--detach','--wait','--wait-timeout','120','postgres','rabbitmq')
 # Queue fixture FIRST; API then consumes the queued message after explicit migrations.
 $published=& dotnet run --project (Join-Path $applicationRoot 'tools/BrokerFixture') --configuration Release
 if($LASTEXITCODE -ne 0){throw 'Fixture publication failed.'};$jobId=@($published)[-1].Trim();$parsed=[Guid]::Empty;if(![Guid]::TryParse($jobId,[ref]$parsed)){throw 'Fixture ID was invalid.'}
 Compose -Arguments @('up','--detach','job-search-api')
 $null=WaitHttp '/health/ready';$null=WaitHttp '/health/ingestion';$job=WaitHttp ('/api/jobs/'+$jobId);if($job.id -ne $jobId){throw 'Projection identity mismatch.'}
 $apiContainer=@(Compose -Arguments @('ps','--quiet','job-search-api'))[-1]
 $uid=@(Docker -Arguments @('exec',$apiContainer,'id','-u'))[-1];if($uid -eq '0'){throw 'Runtime is root.'}
 $list=WaitHttp '/api/jobs';if(@($list.items|Where-Object{$_.id -eq $jobId}).Count -ne 1){throw 'Fixture missing from list.'}
 Compose -Arguments @('restart','rabbitmq');$null=WaitHttp '/health/ingestion'
 Compose -Arguments @('stop','job-search-api','postgres')
 Compose -Arguments @('up','--detach','job-search-api');$persisted=WaitHttp ('/api/jobs/'+$jobId);if($persisted.id -ne $jobId){throw 'Restart did not preserve job.'}
 Write-Output 'PASS: independent images, migrations, queued fixture-to-GET, non-root runtime, broker reconnect and persistent search restart.'
 } finally {
 try {
  if(Test-Path -LiteralPath $verificationEnv){try{Compose -Arguments @('down','--volumes','--remove-orphans')}catch{Write-Warning 'Owned verification Compose cleanup needs inspection.'}}
  if($networkCreated){$networkInfo=(Docker -Arguments @('network','inspect',$verificationNetwork)|Out-String|ConvertFrom-Json);$label=$networkInfo[0].Labels.'jobsearch.verifier';if($label -eq $verificationProject){Docker -Arguments @('network','rm',$verificationNetwork)|Out-Null}}
 } finally {
  if(Test-Path -LiteralPath $verificationEnv){Remove-Item -LiteralPath $verificationEnv}
  foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}
 }
}
