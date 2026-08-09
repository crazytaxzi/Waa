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
Initialize-WaaConversation
$startupIntake = Invoke-WaaDownloadsScan
$script:LastIntakeScan = Get-Date
$web = [IO.Path]::GetFullPath((Join-Path $Root 'web'))

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
    param(
        $Stream,
        [int]$Status,
        [string]$Type,
        [byte[]]$Bytes
    )

    $reasons = @{
        200 = 'OK'
        201 = 'Created'
        204 = 'No Content'
        400 = 'Bad Request'
        404 = 'Not Found'
        409 = 'Conflict'
        500 = 'Internal Server Error'
    }
    $reason = $reasons[$Status]
    $head = "HTTP/1.1 $Status $reason`r`n" +
        "Content-Type: $Type`r`n" +
        "Content-Length: $($Bytes.Length)`r`n" +
        "Cache-Control: no-store`r`n" +
        "Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'`r`n" +
        "X-Content-Type-Options: nosniff`r`n" +
        "Referrer-Policy: no-referrer`r`n" +
        "Access-Control-Allow-Origin: http://127.0.0.1`r`n" +
        "Connection: close`r`n`r`n"

    try {
        $headerBytes = [Text.Encoding]::ASCII.GetBytes($head)
        $Stream.Write($headerBytes, 0, $headerBytes.Length)
        if ($Bytes.Length -gt 0) {
            $Stream.Write($Bytes, 0, $Bytes.Length)
        }
        $Stream.Flush()
        return $true
    }
    catch {
        if (Test-WaaClientDisconnect $_) {
            # Browsers routinely cancel requests during navigation, refresh, or cache races.
            # That client is gone; the WAA server itself should keep running.
            return $false
        }
        throw
    }
}

function Send-Json {
    param($Stream, [int]$Code, $Data)
    $json = $Data | ConvertTo-Json -Depth 20 -Compress
    return Send-Response $Stream $Code 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}

function Send-JsonArray {
    param($Stream, [int]$Code, $Data)
    $json = ConvertTo-Json -InputObject @($Data) -Depth 20 -Compress
    return Send-Response $Stream $Code 'application/json; charset=utf-8' ([Text.Encoding]::UTF8.GetBytes($json))
}

function Read-Request {
    param($Client)

    $stream = $Client.GetStream()
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $false, 4096, $true)
    $line = $reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($line)) { return $null }

    $parts = $line.Split(' ')
    $headers = @{}
    while (($line = $reader.ReadLine()) -ne '') {
        $index = $line.IndexOf(':')
        if ($index -gt 0) {
            $headers[$line.Substring(0, $index).ToLowerInvariant()] = $line.Substring($index + 1).Trim()
        }
    }

    $body = ''
    if ($headers['content-length']) {
        $buffer = New-Object char[] ([int]$headers['content-length'])
        $count = $reader.ReadBlock($buffer, 0, $buffer.Length)
        if ($count -gt 0) {
            $body = -join $buffer[0..($count - 1)]
        }
    }

    return @{
        method = $parts[0]
        target = $parts[1]
        headers = $headers
        body = $body
        stream = $stream
    }
}

function Refresh-DownloadsIfDue {
    if (((Get-Date) - $script:LastIntakeScan).TotalSeconds -ge 60) {
        try { Invoke-WaaDownloadsScan | Out-Null } catch { }
        $script:LastIntakeScan = Get-Date
    }
}

$listener = $null
$port = 8765
for ($candidate = 8765; $candidate -le 8775; $candidate++) {
    try {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $candidate)
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
if (-not $NoBrowser) { Start-Process $url }

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $request = Read-Request $client
            if ($null -eq $request) { continue }

            Refresh-DownloadsIfDue
            $uri = [Uri]("http://127.0.0.1" + $request.target)
            $path = [Uri]::UnescapeDataString($uri.AbsolutePath)
            $method = $request.method

            try {
                if ($path.StartsWith('/api/')) {
                    $body = @{}
                    if (-not [string]::IsNullOrWhiteSpace($request.body)) {
                        $object = $request.body | ConvertFrom-Json
                        if ($null -ne $object) {
                            foreach ($property in $object.PSObject.Properties) {
                                $body[$property.Name] = $property.Value
                            }
                        }
                    }

                    if ($method -eq 'GET' -and $path -eq '/api/health') {
                        Send-Json $request.stream 200 @{ ok = $true; integrity = $state.integrity; read_only = $state.read_only } | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/dashboard') {
                        Send-Json $request.stream 200 (Get-Dashboard) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/drivers') {
                        Send-JsonArray $request.stream 200 @(Get-CurrentDrivers) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -match '^/api/drivers/(\d+)$') {
                        Send-Json $request.stream 200 (Get-DriverCard ([int]$Matches[1])) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -match '^/api/drivers/(\d+)/action$') {
                        Send-Json $request.stream 200 (Save-DriverAction ([int]$Matches[1]) $body) | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -match '^/api/drivers/(\d+)/conversation$') {
                        Send-Json $request.stream 200 (Get-WaaConversation -DriverId ([int]$Matches[1])) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -match '^/api/drivers/(\d+)/conversation$') {
                        Send-Json $request.stream 200 (Save-WaaConversation -DriverId ([int]$Matches[1]) -Body $body) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/import/preview') {
                        if ($body.type -and $body.type -ne 'pta') {
                            throw 'Rolling 7-Day and Missing BOL reports are managed automatically from Downloads. PTA is paste-only.'
                        }
                        Send-Json $request.stream 200 (Get-ImportPreview $body.raw 'PTA paste' 'pta') | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/import/commit') {
                        if ($body.type -and $body.type -ne 'pta') {
                            throw 'Rolling 7-Day and Missing BOL reports are managed automatically from Downloads. PTA is paste-only.'
                        }
                        Send-Json $request.stream 201 (Import-WaaData $body.raw 'PTA paste' 'pta') | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/report-intake') {
                        Send-Json $request.stream 200 (Get-WaaReportIntakeStatus) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/report-intake/scan') {
                        $scan = Invoke-WaaDownloadsScan
                        $script:LastIntakeScan = Get-Date
                        Send-Json $request.stream 200 $scan | Out-Null
                    }
                    elseif ($method -eq 'GET' -and $path -eq '/api/data-quality') {
                        Send-Json $request.stream 200 (Get-DataQuality) | Out-Null
                    }
                    elseif ($method -eq 'POST' -and $path -eq '/api/identity/resolve') {
                        Send-Json $request.stream 200 (Resolve-Identity ([int]$body.issue_id) ([int]$body.driver_id)) | Out-Null
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
                        $bolSql = "WITH t AS(SELECT *,row_number()OVER(PARTITION BY driver_id ORDER BY observed_at DESC,id DESC)rn FROM truck_history)" +
                            " SELECT b.*,d.full_name,t.truck FROM missing_bols b" +
                            " LEFT JOIN drivers d ON d.id=b.driver_id" +
                            " LEFT JOIN t ON t.driver_id=b.driver_id AND t.rn=1" +
                            " ORDER BY b.mentioned_at,b.empty_call_date;"
                        Send-JsonArray $request.stream 200 @(Invoke-Sql $bolSql -Json) | Out-Null
                    }
                    else {
                        Send-Json $request.stream 404 @{ error = 'API route not found' } | Out-Null
                    }
                }
                else {
                    $relative = if ($path -eq '/') { 'index.html' } else { $path.TrimStart('/') }
                    if ($relative.Contains('..') -or $relative.Contains('\')) { throw 'Invalid path' }

                    $file = [IO.Path]::GetFullPath((Join-Path $web $relative))
                    if (-not $file.StartsWith($web, [StringComparison]::OrdinalIgnoreCase) -or
                        -not (Test-Path $file -PathType Leaf)) {
                        Send-Json $request.stream 404 @{ error = 'Not found' } | Out-Null
                    }
                    else {
                        $extension = [IO.Path]::GetExtension($file)
                        $mime = @{
                            '.html' = 'text/html; charset=utf-8'
                            '.css' = 'text/css; charset=utf-8'
                            '.js' = 'text/javascript; charset=utf-8'
                            '.svg' = 'image/svg+xml'
                        }[$extension]
                        if ([string]::IsNullOrWhiteSpace($mime)) { $mime = 'application/octet-stream' }
                        Send-Response $request.stream 200 $mime ([IO.File]::ReadAllBytes($file)) | Out-Null
                    }
                }
            }
            catch {
                if (Test-WaaClientDisconnect $_) {
                    continue
                }

                $code = if ($_.Exception.Message -like 'Duplicate*') { 409 } else { 400 }
                Send-Json $request.stream $code @{ error = $_.Exception.Message } | Out-Null
            }
        }
        catch {
            if (-not (Test-WaaClientDisconnect $_)) {
                Write-Warning ("Request failed without stopping WAA: " + $_.Exception.Message)
            }
        }
        finally {
            try { $client.Dispose() } catch { }
        }
    }
}
finally {
    if ($null -ne $listener) { $listener.Stop() }
}
