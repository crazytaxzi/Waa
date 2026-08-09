[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$DataRoot,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'Waa.psm1') -Force
. (Join-Path $PSScriptRoot 'ReportParsing.ps1')
. (Join-Path $PSScriptRoot 'ReportIntake.ps1')
. (Join-Path $PSScriptRoot 'Conversation.ps1')

$state = Initialize-Waa $Root $DataRoot
Initialize-WaaReportIntake $DataRoot
$startupIntake = Invoke-WaaDownloadsScan
$startupIdentity = Repair-WaaDriverIdentity
$script:LastIntakeScan = Get-Date
$script:IntakeRunner = $null
$script:IntakeAsync = $null
$web = [IO.Path]::GetFullPath((Join-Path $Root 'web'))
$script:StaticCache = @{}
foreach ($asset in @('index.html','styles.css','app.js')) {
    $assetPath = Join-Path $web $asset
    if (Test-Path -LiteralPath $assetPath) { $script:StaticCache[$assetPath] = [IO.File]::ReadAllBytes($assetPath) }
}

function Update-WaaBackgroundScan {
    if ($null -ne $script:IntakeAsync) {
        if (-not $script:IntakeAsync.IsCompleted) { return }
        try { [void]$script:IntakeRunner.EndInvoke($script:IntakeAsync) }
        catch { Write-Warning ("Background report scan failed: " + $_.Exception.Message) }
        finally {
            $script:IntakeRunner.Dispose()
            $script:IntakeRunner = $null
            $script:IntakeAsync = $null
            $script:LastIntakeScan = Get-Date
        }
    }
    if (((Get-Date)-$script:LastIntakeScan).TotalSeconds -lt 60) { return }
    $scriptText = @'
param($ScanRoot,$ScanDataRoot)
$ErrorActionPreference='Stop'
Import-Module (Join-Path $ScanRoot 'src/Waa.psm1') -Force
. (Join-Path $ScanRoot 'src/ReportParsing.ps1')
. (Join-Path $ScanRoot 'src/ReportIntake.ps1')
Initialize-Waa $ScanRoot $ScanDataRoot -SkipStartupBackup | Out-Null
Initialize-WaaReportIntake $ScanDataRoot
$scan=Invoke-WaaDownloadsScan
if($scan.results.idle.imported -or $scan.results.bol.imported){Repair-WaaDriverIdentity|Out-Null}
'@
    $script:IntakeRunner = [PowerShell]::Create()
    [void]$script:IntakeRunner.AddScript($scriptText).AddArgument($Root).AddArgument($DataRoot)
    $script:IntakeAsync = $script:IntakeRunner.BeginInvoke()
}

function Test-WaaClientDisconnect {
    param([Parameter(Mandatory = $true)]$ErrorRecord)

    $exception = $ErrorRecord.Exception
    while ($null -ne $exception) {
        if ($exception -is [IO.IOException] -or
            $exception -is [Net.Sockets.SocketException] -or
            $exception -is [ObjectDisposedException]) {
            return $true
        }

        $message = [string]$exception.Message
        if ($message -match '(?i)(transport connection|connection.*aborted|forcibly closed|broken pipe|connection reset|disposed object)') {
            return $true
        }
        $exception = $exception.InnerException
    }
    return $false
}

function Send-Response {
    param($Stream,[int]$Status,[string]$Type,[byte[]]$Bytes,[switch]$Static,[switch]$Revalidate)

    $reasons = @{200='OK';201='Created';204='No Content';400='Bad Request';404='Not Found';409='Conflict';500='Internal Server Error'}
    $reason = $reasons[$Status]
    $head = "HTTP/1.1 $Status $reason`r`n" +
        "Content-Type: $Type`r`n" +
        "Content-Length: $($Bytes.Length)`r`n" +
        $(if ($Revalidate) { "Cache-Control: no-cache`r`n" } elseif ($Static) { "Cache-Control: public, max-age=300`r`n" } else { "Cache-Control: no-store`r`n" }) +
        "Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'`r`n" +
        "X-Content-Type-Options: nosniff`r`n" +
        "Referrer-Policy: no-referrer`r`n" +
        "Access-Control-Allow-Origin: http://127.0.0.1`r`n" +
        "Connection: close`r`n`r`n"

    try {
        $headerBytes = [Text.Encoding]::ASCII.GetBytes($head)
        $Stream.Write($headerBytes,0,$headerBytes.Length)
        if ($Bytes.Length -gt 0) { $Stream.Write($Bytes,0,$Bytes.Length) }
        $Stream.Flush()
        return $true
    }
    catch {
        if (Test-WaaClientDisconnect $_) { return $false }
        throw
    }
}

function Send-Json {
    param($Stream,[int]$Code,$Data)
    $json = $Data | ConvertTo-Json -Depth 20 -Compress
    return Send-Response $Stream $Code 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}

function Send-JsonArray {
    param($Stream,[int]$Code,$Data)
    $json = ConvertTo-Json -InputObject @($Data) -Depth 20 -Compress
    return Send-Response $Stream $Code 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}

function Read-Request {
    param($Client)

    $stream = $Client.GetStream()
    $stream.ReadTimeout = 15000
    $stream.WriteTimeout = 15000
    $headerBuffer = [Collections.Generic.List[byte]]::new(2048)
    $matched = 0
    [byte[]]$terminator = @(13,10,13,10)
    while ($matched -lt 4) {
        $next = $stream.ReadByte()
        if ($next -lt 0) { return $null }
        [void]$headerBuffer.Add([byte]$next)
        if ($headerBuffer.Count -gt 32768) { throw 'HTTP headers are too large' }
        if ($next -eq $terminator[$matched]) { $matched++ }
        elseif ($next -eq 13) { $matched = 1 }
        else { $matched = 0 }
    }
    $headerText = [Text.Encoding]::ASCII.GetString($headerBuffer.ToArray(),0,$headerBuffer.Count-4)
    $lines = $headerText -split "`r`n"
    if ($lines.Count -eq 0 -or [string]::IsNullOrWhiteSpace($lines[0])) { return $null }
    $parts = $lines[0].Split(' ')
    if ($parts.Count -lt 3) { throw 'Malformed HTTP request line' }
    $headers = @{}
    foreach ($line in $lines | Select-Object -Skip 1) {
        $index = $line.IndexOf(':')
        if ($index -gt 0) {
            $headers[$line.Substring(0,$index).ToLowerInvariant()] = $line.Substring($index+1).Trim()
        }
    }

    $body = ''
    if ($headers['content-length']) {
        $length = 0
        if (-not [int]::TryParse($headers['content-length'],[ref]$length) -or $length -lt 0 -or $length -gt 26214400) {
            throw 'Invalid or oversized request body'
        }
        $buffer = New-Object byte[] $length
        $offset = 0
        while ($offset -lt $length) {
            $count = $stream.Read($buffer,$offset,$length-$offset)
            if ($count -le 0) { throw 'Incomplete request body' }
            $offset += $count
        }
        if ($offset -gt 0) { $body = [Text.Encoding]::UTF8.GetString($buffer,0,$offset) }
    }

    return @{method=$parts[0];target=$parts[1];headers=$headers;body=$body;stream=$stream}
}

$listener = $null
$port = 8765
for ($candidate=8765; $candidate -le 8775; $candidate++) {
    try {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$candidate)
        $listener.Start()
        $port = $candidate
        break
    }
    catch {
        if ($candidate -eq 8775) { throw }
    }
}

$url = "http://127.0.0.1:$port/"
Write-Host "WAA console ready at $url" -ForegroundColor Green
Write-Host "Database: $($state.db) | Integrity: $($state.integrity)"
Write-Host "Downloads intake: $((Get-WaaReportIntakeStatus).downloads_path)"
if ($startupIdentity.merged -gt 0 -or $startupIdentity.evidence -gt 0) {
    Write-Host "Driver identity: $($startupIdentity.evidence) evidence sets | $($startupIdentity.merged) legacy fragments merged"
}
if (-not $NoBrowser) { Start-Process $url }

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $request = Read-Request $client
            if ($null -eq $request) { continue }
            Update-WaaBackgroundScan

            $uri = [Uri]("http://127.0.0.1" + $request.target)
            $path = [Uri]::UnescapeDataString($uri.AbsolutePath)
            $method = $request.method

            try {
                if ($path.StartsWith('/api/')) {
                    $body = @{}
                    if (-not [string]::IsNullOrWhiteSpace($request.body)) {
                        $object = $request.body | ConvertFrom-Json
                        if ($null -ne $object) {
                            foreach ($property in $object.PSObject.Properties) { $body[$property.Name] = $property.Value }
                        }
                    }

                    if ($method -eq 'GET' -and $path -eq '/api/health') {
                        Send-Json $request.stream 200 @{ok=$true;integrity=$state.integrity;read_only=$state.read_only} | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/dashboard') {
                        Send-Json $request.stream 200 (Get-Dashboard) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/drivers') {
                        Send-JsonArray $request.stream 200 @(Get-CurrentDrivers) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -match '^/api/drivers/(\d+)/context$') {
                        $driverId = [int]$Matches[1]
                        Send-Json $request.stream 200 @{card=Get-DriverCard $driverId;conversation=Get-WaaConversation -DriverId $driverId} | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -match '^/api/drivers/(\d+)/action$') {
                        Send-Json $request.stream 200 (Save-DriverAction ([int]$Matches[1]) $body) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -match '^/api/drivers/(\d+)/conversation$') {
                        Send-Json $request.stream 200 (Save-WaaConversation -DriverId ([int]$Matches[1]) -Body $body) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/import/preview') {
                        if ($body.type -and $body.type -ne 'pta') { throw 'Rolling 7-Day and Missing BOL reports are managed automatically from Downloads. PTA is paste-only.' }
                        Send-Json $request.stream 200 (Get-ImportPreview ([string]$body.raw) 'PTA paste' 'pta') | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/import/commit') {
                        if ($body.type -and $body.type -ne 'pta') { throw 'Rolling 7-Day and Missing BOL reports are managed automatically from Downloads. PTA is paste-only.' }
                        $result = Import-WaaData ([string]$body.raw) 'PTA paste' 'pta'
                        Repair-WaaDriverIdentity | Out-Null
                        Send-Json $request.stream 201 $result | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/report-intake') {
                        Send-Json $request.stream 200 (Get-WaaReportIntakeStatus) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/report-intake/scan') {
                        if ($null -ne $script:IntakeAsync -and -not $script:IntakeAsync.IsCompleted) { throw 'A report scan is already running.' }
                        $scan = Invoke-WaaDownloadsScan
                        $identity = Repair-WaaDriverIdentity
                        $script:LastIntakeScan = Get-Date
                        $scan.identity = $identity
                        Send-Json $request.stream 200 $scan | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/data-quality') {
                        Send-Json $request.stream 200 (Get-DataQuality) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/organizer') {
                        Send-Json $request.stream 200 (Get-Organizer) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/activity') {
                        $query = @{}
                        foreach ($pair in $uri.Query.TrimStart('?').Split('&',[StringSplitOptions]::RemoveEmptyEntries)) {
                            $parts = $pair.Split('=',2)
                            if ($parts.Count -eq 2) { $query[[Uri]::UnescapeDataString($parts[0])] = [Uri]::UnescapeDataString($parts[1].Replace('+',' ')) }
                        }
                        $startDate = [datetime]::MinValue
                        $endDate = [datetime]::MinValue
                        if (-not [datetime]::TryParse([string]$query.start,[ref]$startDate) -or
                            -not [datetime]::TryParse([string]$query.end,[ref]$endDate) -or $endDate -le $startDate) {
                            throw 'Valid activity start and end times are required'
                        }
                        $startUtc = $startDate.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
                        $endUtc = $endDate.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
                        Send-JsonArray $request.stream 200 @(Get-DailyActivity $startUtc $endUtc) | Out-Null
                    }
                    elseif ($method -eq 'DELETE' -and $path -match '^/api/activity/(\d+)$') {
                        Send-Json $request.stream 200 (Remove-DailyActivity ([int]$Matches[1])) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/identity/resolve') {
                        $resolved = Resolve-Identity ([int]$body.issue_id) ([int]$body.driver_id)
                        Repair-WaaDriverIdentity | Out-Null
                        Send-Json $request.stream 200 $resolved | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/transition') {
                        Send-Json $request.stream 200 (Get-Transition) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/transition/regenerate') {
                        Send-Json $request.stream 200 (Get-Transition -Regenerate) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/transition') {
                        Send-Json $request.stream 200 (Save-Transition $body.body) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/safety/random') {
                        $except = 0
                        if ($uri.Query -match 'except=(\d+)') { $except = [int]$Matches[1] }
                        Send-Json $request.stream 200 (Get-SafetyNote $except) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/backup') {
                        Send-Json $request.stream 201 (Backup-Waa) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/restore') {
                        Send-Json $request.stream 200 (Restore-Waa $body.name) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/bols') {
                        $bolSql = "WITH t AS(SELECT *,row_number()OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC)rn FROM truck_history) SELECT b.*,d.full_name,t.truck FROM missing_bols b LEFT JOIN drivers d ON d.id=b.driver_id LEFT JOIN t ON t.driver_id=b.driver_id AND t.rn=1 ORDER BY b.mentioned_at,b.empty_call_date;"
                        Send-JsonArray $request.stream 200 @(Invoke-Sql $bolSql -Json) | Out-Null
                    }
                    else {
                        Send-Json $request.stream 404 @{error='API route not found'} | Out-Null
                    }
                }
                else {
                    $relative = if ($path -eq '/') { 'index.html' } else { $path.TrimStart('/') }
                    if ($relative.Contains('..') -or $relative.Contains('\')) { throw 'Invalid path' }
                    $file = [IO.Path]::GetFullPath((Join-Path $web $relative))
                    if (-not $file.StartsWith($web,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path $file -PathType Leaf)) {
                        Send-Json $request.stream 404 @{error='Not found'} | Out-Null
                    }
                    else {
                        $extension = [IO.Path]::GetExtension($file)
                        $mime = @{'.html'='text/html; charset=utf-8';'.css'='text/css; charset=utf-8';'.js'='text/javascript; charset=utf-8';'.svg'='image/svg+xml'}[$extension]
                        if ([string]::IsNullOrWhiteSpace($mime)) { $mime='application/octet-stream' }
                        $bytes = if ($script:StaticCache.ContainsKey($file)) { $script:StaticCache[$file] } else { [IO.File]::ReadAllBytes($file) }
                        if ($extension -eq '.html') { Send-Response $request.stream 200 $mime $bytes -Revalidate | Out-Null }
                        else { Send-Response $request.stream 200 $mime $bytes -Static | Out-Null }
                    }
                }
            }
            catch {
                if (Test-WaaClientDisconnect $_) { continue }
                $code = if ($_.Exception.Message -like 'Duplicate*') { 409 } else { 400 }
                Send-Json $request.stream $code @{error=$_.Exception.Message} | Out-Null
            }
        }
        catch {
            if (-not (Test-WaaClientDisconnect $_)) { Write-Warning ("Request failed without stopping WAA: " + $_.Exception.Message) }
        }
        finally { try { $client.Dispose() } catch { } }
    }
}
finally {
    if ($null -ne $script:IntakeRunner) { try { $script:IntakeRunner.Stop();$script:IntakeRunner.Dispose() } catch { } }
    if ($null -ne $listener) { $listener.Stop() }
}
