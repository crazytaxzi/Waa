Set-StrictMode -Version Latest

$script:WaaReportDataRoot = $null

function ConvertTo-WaaSqlLiteral {
    param([AllowNull()]$Value)

    if ($null -eq $Value) { return 'NULL' }
    if ($Value -is [bool]) {
        if ($Value) { return '1' }
        return '0'
    }
    if ($Value -is [byte] -or
        $Value -is [int16] -or
        $Value -is [int] -or
        $Value -is [long] -or
        $Value -is [single] -or
        $Value -is [double] -or
        $Value -is [decimal]) {
        return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    }

    return "'" + ([string]$Value).Replace("'", "''").Replace([string][char]0, '') + "'"
}

function Get-WaaDownloadsPath {
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.NameSpace('shell:Downloads')
        if ($null -ne $folder -and
            $null -ne $folder.Self -and
            -not [string]::IsNullOrWhiteSpace([string]$folder.Self.Path) -and
            (Test-Path -LiteralPath $folder.Self.Path)) {
            return [string]$folder.Self.Path
        }
    }
    catch {
        # Fall back to the conventional profile Downloads folder.
    }

    return (Join-Path $env:USERPROFILE 'Downloads')
}

function Get-WaaReportRoot {
    $base = $script:WaaReportDataRoot
    if ([string]::IsNullOrWhiteSpace([string]$base)) {
        $base = Join-Path $env:LOCALAPPDATA 'Waa'
    }

    $root = Join-Path $base 'reports'
    [IO.Directory]::CreateDirectory((Join-Path $root 'idle')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $root 'missing-bol')) | Out-Null
    return $root
}

function Initialize-WaaReportIntake {
    param([string]$DataRoot)

    if (-not [string]::IsNullOrWhiteSpace($DataRoot)) {
        $script:WaaReportDataRoot = $DataRoot
    }

    Get-WaaReportRoot | Out-Null

    $schema = @'
CREATE TABLE IF NOT EXISTS report_intake_status(
  report_type TEXT PRIMARY KEY,
  downloads_path TEXT,
  source_name TEXT,
  source_path TEXT,
  source_modified_utc TEXT,
  source_hash TEXT,
  managed_path TEXT,
  import_batch_id INTEGER REFERENCES import_batches(id),
  status TEXT NOT NULL DEFAULT 'Waiting',
  detail TEXT,
  scanned_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
INSERT OR IGNORE INTO report_intake_status(report_type,status) VALUES('idle','Waiting'),('bol','Waiting');
'@
    Invoke-Sql $schema -AllowWrite | Out-Null
}

function Get-WaaFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Get-WaaTextSha256 {
    param([Parameter(Mandatory = $true)][string]$Text)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Read-WaaTextFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)

    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe) {
        return [Text.Encoding]::Unicode.GetString($bytes).TrimStart([char]0xfeff)
    }
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xfe -and $bytes[1] -eq 0xff) {
        return [Text.Encoding]::BigEndianUnicode.GetString($bytes).TrimStart([char]0xfeff)
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) {
        return [Text.Encoding]::UTF8.GetString($bytes, 3, $bytes.Length - 3)
    }

    return [Text.Encoding]::UTF8.GetString($bytes)
}

function Get-WaaZipEntryText {
    param(
        [Parameter(Mandatory = $true)]$Zip,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { return $null }

    $stream = $entry.Open()
    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
    try {
        $text = $reader.ReadToEnd()
        return $text
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-WaaColumnIndexFromRef {
    param([Parameter(Mandatory = $true)][string]$Ref)

    if ($Ref -notmatch '^([A-Za-z]+)') { return -1 }

    $letters = $Matches[1].ToUpperInvariant().ToCharArray()
    $n = 0
    foreach ($ch in $letters) {
        $n = ($n * 26) + ([int]$ch - [int][char]'A' + 1)
    }

    return ($n - 1)
}

function Get-WaaWorkbookEntryName {
    param([Parameter(Mandatory = $true)][string]$Target)

    $value = $Target.Replace('\', '/')
    if ($value.StartsWith('/')) {
        return $value.TrimStart('/')
    }
    if ($value.StartsWith('xl/')) {
        return $value
    }

    while ($value.StartsWith('../')) {
        $value = $value.Substring(3)
    }
    return ('xl/' + $value.TrimStart('/'))
}

function Read-WaaXlsxSheets {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)

    try {
        $shared = @()
        $sharedText = Get-WaaZipEntryText -Zip $zip -Name 'xl/sharedStrings.xml'
        if (-not [string]::IsNullOrWhiteSpace($sharedText)) {
            [xml]$sharedXml = $sharedText
            foreach ($si in $sharedXml.SelectNodes("//*[local-name()='si']")) {
                $parts = @($si.SelectNodes(".//*[local-name()='t']") | ForEach-Object { [string]$_.InnerText })
                $shared += ($parts -join '')
            }
        }

        $workbookText = Get-WaaZipEntryText -Zip $zip -Name 'xl/workbook.xml'
        $relationsText = Get-WaaZipEntryText -Zip $zip -Name 'xl/_rels/workbook.xml.rels'
        if ([string]::IsNullOrWhiteSpace($workbookText) -or [string]::IsNullOrWhiteSpace($relationsText)) {
            throw 'The XLSX package is missing workbook relationship data.'
        }

        [xml]$workbookXml = $workbookText
        [xml]$relationsXml = $relationsText

        $relationMap = @{}
        foreach ($relation in $relationsXml.SelectNodes("//*[local-name()='Relationship']")) {
            $relationMap[[string]$relation.Id] = [string]$relation.Target
        }

        $sheets = @()
        foreach ($sheet in $workbookXml.SelectNodes("//*[local-name()='sheet']")) {
            $relationshipId = $sheet.GetAttribute(
                'id',
                'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
            )
            if ([string]::IsNullOrWhiteSpace($relationshipId)) { continue }
            if (-not $relationMap.ContainsKey($relationshipId)) { continue }

            $entryName = Get-WaaWorkbookEntryName -Target $relationMap[$relationshipId]
            $sheetText = Get-WaaZipEntryText -Zip $zip -Name $entryName
            if ([string]::IsNullOrWhiteSpace($sheetText)) { continue }

            [xml]$sheetXml = $sheetText
            $rows = @()

            foreach ($rowNode in $sheetXml.SelectNodes("//*[local-name()='sheetData']/*[local-name()='row']")) {
                $cellValues = @{}
                $maxIndex = -1

                foreach ($cell in $rowNode.SelectNodes("./*[local-name()='c']")) {
                    $index = Get-WaaColumnIndexFromRef -Ref ([string]$cell.r)
                    if ($index -lt 0) { continue }
                    if ($index -gt $maxIndex) { $maxIndex = $index }

                    $cellType = [string]$cell.t
                    $value = ''

                    if ($cellType -eq 'inlineStr') {
                        $inlineParts = @($cell.SelectNodes(".//*[local-name()='t']") | ForEach-Object { [string]$_.InnerText })
                        $value = $inlineParts -join ''
                    }
                    else {
                        $valueNode = $cell.SelectSingleNode("./*[local-name()='v']")
                        if ($null -ne $valueNode) {
                            $value = [string]$valueNode.InnerText
                        }

                        if ($cellType -eq 's' -and $value -match '^\d+$') {
                            $sharedIndex = [int]$value
                            if ($sharedIndex -ge 0 -and $sharedIndex -lt $shared.Count) {
                                $value = [string]$shared[$sharedIndex]
                            }
                        }
                        elseif ($cellType -eq 'b') {
                            if ($value -eq '1') { $value = 'TRUE' } else { $value = 'FALSE' }
                        }
                    }

                    $cellValues[$index] = $value
                }

                if ($maxIndex -ge 0) {
                    $row = [string[]]::new($maxIndex + 1)
                    for ($i = 0; $i -le $maxIndex; $i++) {
                        if ($cellValues.ContainsKey($i)) {
                            $row[$i] = [string]$cellValues[$i]
                        }
                        else {
                            $row[$i] = ''
                        }
                    }
                    $rows += ,$row
                }
            }

            $sheets += ,@{
                name = [string]$sheet.name
                rows = $rows
            }
        }

        return $sheets
    }
    finally {
        $zip.Dispose()
    }
}

function Normalize-WaaHeader {
    param([AllowNull()][string]$Text)
    return [regex]::Replace(([string]$Text).Trim().ToLowerInvariant(), '[^a-z0-9]', '')
}

function Find-WaaColumn {
    param(
        [Parameter(Mandatory = $true)][object[]]$Headers,
        [Parameter(Mandatory = $true)][string[]]$Aliases
    )

    for ($i = 0; $i -lt $Headers.Count; $i++) {
        $header = Normalize-WaaHeader -Text ([string]$Headers[$i])
        foreach ($alias in $Aliases) {
            if ($header -eq (Normalize-WaaHeader -Text $alias)) {
                return $i
            }
        }
    }

    return -1
}

function Convert-WaaExcelDate {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $Value }

    $number = 0.0
    $parsed = [double]::TryParse(
        $Value,
        [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$number
    )
    if ($parsed -and $number -ge 20000 -and $number -le 100000) {
        try {
            return [datetime]::FromOADate($number).ToString('MM/dd/yyyy HH:mm')
        }
        catch {
            return $Value
        }
    }

    return $Value
}

function Clean-WaaField {
    param([AllowNull()]$Value)
    return ([string]$Value).Replace("`t", ' ').Replace("`r", ' ').Replace("`n", ' ').Trim()
}

function Get-WaaCanonicalTextFromRows {
    param(
        [Parameter(Mandatory = $true)][object[]]$Rows,
        [Parameter(Mandatory = $true)][ValidateSet('idle', 'bol')][string]$Type
    )

    if ($null -eq $Rows -or $Rows.Count -eq 0) {
        throw 'The workbook/report contains no rows.'
    }

    $headerIndex = -1
    $map = $null
    $maxRowsToInspect = [Math]::Min(60, $Rows.Count)

    for ($rowIndex = 0; $rowIndex -lt $maxRowsToInspect; $rowIndex++) {
        $headers = @($Rows[$rowIndex])

        if ($Type -eq 'idle') {
            $candidate = @{
                group   = Find-WaaColumn -Headers $headers -Aliases @('Group by  (copy)', 'Group by (copy)', 'Driver')
                unit    = Find-WaaColumn -Headers $headers -Aliases @('Unit Code', 'Truck', 'Unit')
                week    = Find-WaaColumn -Headers $headers -Aliases @('Week Start Date')
                rolling = Find-WaaColumn -Headers $headers -Aliases @('Rolling 7 Day Start Date')
                engine  = Find-WaaColumn -Headers $headers -Aliases @('[Rolling 7 Day Engine Time]/60', 'Rolling 7 Day Engine Time/60')
                idle    = Find-WaaColumn -Headers $headers -Aliases @('[Rolling 7 Day Idle Time]/60', 'Rolling 7 Day Idle Time/60')
                measure = Find-WaaColumn -Headers $headers -Aliases @('Measure Names', 'Measure Name')
            }
            $missing = @($candidate.Values | Where-Object { [int]$_ -lt 0 })
            if ($missing.Count -eq 0) {
                $headerIndex = $rowIndex
                $map = $candidate
                break
            }
        }
        else {
            $candidate = @{
                order       = Find-WaaColumn -Headers $headers -Aliases @('Order #', 'Order', 'Order Number')
                date        = Find-WaaColumn -Headers $headers -Aliases @('Empty Call Date')
                origin      = Find-WaaColumn -Headers $headers -Aliases @('Origin City St', 'Origin City/State', 'Origin')
                destination = Find-WaaColumn -Headers $headers -Aliases @('Destination City St', 'Destination City/State', 'Destination')
                mileage     = Find-WaaColumn -Headers $headers -Aliases @('Loaded Miles', 'Order Level Order Miles', 'Miles')
                type        = Find-WaaColumn -Headers $headers -Aliases @('Rev Type', 'BOL Type', 'Revenue Type')
                code        = Find-WaaColumn -Headers $headers -Aliases @('Last Dispatch Driver cd', 'Last Dispatch Driver Code', 'Driver cd')
                name        = Find-WaaColumn -Headers $headers -Aliases @('Last Dispatch Driver nm', 'Last Dispatch Driver Name', 'Driver Name')
            }
            if ($candidate.order -ge 0 -and $candidate.date -ge 0 -and $candidate.code -ge 0 -and $candidate.name -ge 0) {
                $headerIndex = $rowIndex
                $map = $candidate
                break
            }
        }
    }

    if ($headerIndex -lt 0) {
        throw "No $Type report header was found in the workbook/report."
    }

    $output = [System.Collections.Generic.List[string]]::new()

    if ($Type -eq 'idle') {
        $canonicalHeaders = @(
            'Group by  (copy)',
            'Unit Code',
            'Week Start Date',
            'Rolling 7 Day Start Date',
            '[Rolling 7 Day Engine Time]/60',
            '[Rolling 7 Day Idle Time]/60',
            'Measure Names'
        )
        $output.Add(($canonicalHeaders -join "`t"))

        for ($rowIndex = $headerIndex + 1; $rowIndex -lt $Rows.Count; $rowIndex++) {
            $row = @($Rows[$rowIndex])
            if ($row.Count -eq 0) { continue }

            $values = @(
                $(if ($map.group -lt $row.Count) { $row[$map.group] } else { '' }),
                $(if ($map.unit -lt $row.Count) { $row[$map.unit] } else { '' }),
                (Convert-WaaExcelDate -Value $(if ($map.week -lt $row.Count) { [string]$row[$map.week] } else { '' })),
                (Convert-WaaExcelDate -Value $(if ($map.rolling -lt $row.Count) { [string]$row[$map.rolling] } else { '' })),
                $(if ($map.engine -lt $row.Count) { $row[$map.engine] } else { '' }),
                $(if ($map.idle -lt $row.Count) { $row[$map.idle] } else { '' }),
                $(if ($map.measure -lt $row.Count) { $row[$map.measure] } else { '' })
            ) | ForEach-Object { Clean-WaaField -Value $_ }

            if (-not [string]::IsNullOrWhiteSpace(($values -join ''))) {
                $output.Add(($values -join "`t"))
            }
        }
    }
    else {
        $canonicalHeaders = @(
            'Order', 'Empty Call Date', 'Origin', 'Destination', 'Mileage', 'BOL Type',
            'Last Dispatch Driver cd', 'Last Dispatch Driver nm'
        ) + @(9..29 | ForEach-Object { "Source $_" })
        $output.Add(($canonicalHeaders -join "`t"))

        for ($rowIndex = $headerIndex + 1; $rowIndex -lt $Rows.Count; $rowIndex++) {
            $row = @($Rows[$rowIndex])
            if ($row.Count -eq 0) { continue }

            $values = @(
                $(if ($map.order -lt $row.Count) { $row[$map.order] } else { '' }),
                (Convert-WaaExcelDate -Value $(if ($map.date -lt $row.Count) { [string]$row[$map.date] } else { '' })),
                $(if ($map.origin -ge 0 -and $map.origin -lt $row.Count) { $row[$map.origin] } else { '' }),
                $(if ($map.destination -ge 0 -and $map.destination -lt $row.Count) { $row[$map.destination] } else { '' }),
                $(if ($map.mileage -ge 0 -and $map.mileage -lt $row.Count) { $row[$map.mileage] } else { '' }),
                $(if ($map.type -ge 0 -and $map.type -lt $row.Count) { $row[$map.type] } else { '' }),
                $(if ($map.code -lt $row.Count) { $row[$map.code] } else { '' }),
                $(if ($map.name -lt $row.Count) { $row[$map.name] } else { '' })
            ) | ForEach-Object { Clean-WaaField -Value $_ }

            if ([string]::IsNullOrWhiteSpace([string]$values[0]) -and
                [string]::IsNullOrWhiteSpace([string]$values[6]) -and
                [string]::IsNullOrWhiteSpace([string]$values[7])) {
                continue
            }

            $values += @('') * 21
            $output.Add(($values -join "`t"))
        }
    }

    if ($output.Count -lt 2) {
        throw "The $Type report header was found, but no data rows were found."
    }

    return ($output -join "`r`n")
}

function Get-WaaCanonicalReportText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('idle', 'bol')][string]$Type
    )

    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -eq '.xlsx') {
        $sheetErrors = @()
        foreach ($sheet in @(Read-WaaXlsxSheets -Path $Path)) {
            try {
                return Get-WaaCanonicalTextFromRows -Rows @($sheet.rows) -Type $Type
            }
            catch {
                $sheetErrors += ($sheet.name + ': ' + $_.Exception.Message)
            }
        }

        $detail = if ($sheetErrors.Count -gt 0) { ' ' + ($sheetErrors -join ' | ') } else { '' }
        throw "No worksheet in $([IO.Path]::GetFileName($Path)) matches the $Type report structure.$detail"
    }

    $raw = Read-WaaTextFile -Path $Path
    $rows = Split-ImportRows $raw
    return Get-WaaCanonicalTextFromRows -Rows @($rows) -Type $Type
}

function Add-WaaDriverSql {
    param(
        [AllowNull()][string]$Code,
        [AllowNull()][string]$Name
    )

    $code = Clean-WaaField -Value $Code
    $name = Clean-WaaField -Value $Name
    if ([string]::IsNullOrWhiteSpace($code)) { return '' }

    $ptaCode = $null
    if (-not [string]::IsNullOrWhiteSpace($name)) {
        $ptaCode = Convert-DriverCode $name
    }

    $codeSql = ConvertTo-WaaSqlLiteral $code
    $nameSql = ConvertTo-WaaSqlLiteral $(if (-not [string]::IsNullOrWhiteSpace($name)) { $name } else { $code })
    $ptaSql = ConvertTo-WaaSqlLiteral $ptaCode

    return @"
INSERT INTO drivers(full_name)
SELECT $nameSql
WHERE NOT EXISTS(
    SELECT 1 FROM driver_aliases
    WHERE alias_type='dispatch_code' AND alias_value=$codeSql COLLATE NOCASE
)
AND NOT EXISTS(
    SELECT 1 FROM drivers WHERE full_name=$nameSql COLLATE NOCASE
)
AND (
    $ptaSql IS NULL OR NOT EXISTS(
        SELECT 1 FROM driver_aliases
        WHERE alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE
    )
);
INSERT INTO driver_aliases(driver_id,alias_type,alias_value,confirmed)
SELECT COALESCE(
    (SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$codeSql COLLATE NOCASE LIMIT 1),
    (SELECT id FROM drivers WHERE full_name=$nameSql COLLATE NOCASE ORDER BY id LIMIT 1),
    (SELECT driver_id FROM driver_aliases WHERE alias_type='pta_code' AND alias_value=$ptaSql COLLATE NOCASE LIMIT 1)
), 'dispatch_code', $codeSql, 0
WHERE NOT EXISTS(
    SELECT 1 FROM driver_aliases
    WHERE alias_type='dispatch_code' AND alias_value=$codeSql COLLATE NOCASE
);
UPDATE drivers
SET full_name=$nameSql
WHERE id=(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$codeSql COLLATE NOCASE LIMIT 1)
AND full_name='Unknown';
"@
}

function Import-WaaManagedReport {
    param(
        [Parameter(Mandatory = $true)][string]$Canonical,
        [Parameter(Mandatory = $true)][string]$Filename,
        [Parameter(Mandatory = $true)][ValidateSet('idle', 'bol')][string]$Type
    )

    $hash = Get-WaaTextSha256 -Text $Canonical
    $hashSql = ConvertTo-WaaSqlLiteral $hash
    $existing = @(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hashSql LIMIT 1;" -Json)
    if ($existing.Count -gt 0) {
        return @{
            status = 'Current'
            imported = $false
            import_batch_id = [int]$existing[0].id
            hash = $hash
            detail = 'Newest report is already imported.'
        }
    }

    $rows = Split-ImportRows $Canonical
    $sql = New-Object Text.StringBuilder
    [void]$sql.AppendLine('BEGIN IMMEDIATE;')

    $rawSql = ConvertTo-WaaSqlLiteral $Canonical
    $fileSql = ConvertTo-WaaSqlLiteral $Filename
    $typeSql = ConvertTo-WaaSqlLiteral $Type
    $sourceRowCount = [Math]::Max(0, $rows.Count - 1)

    [void]$sql.AppendLine(
        "INSERT INTO import_batches(source_hash,import_type,parser_version,filename,source_type,raw_source,row_count,warning_count,error_count) " +
        "VALUES($hashSql,$typeSql,'2.0.1',$fileSql,'downloads',$rawSql,$sourceRowCount,0,0);"
    )

    if ($Type -eq 'idle') {
        for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
            $row = @($rows[$rowIndex])
            if ($row.Count -lt 7) { continue }
            if ([string]$row[6] -ne 'Idle %') { continue }

            $driverParts = ([string]$row[0]) -split ' ', 2
            if ($driverParts.Count -lt 2) { continue }

            $driverCode = $driverParts[0]
            $driverName = $driverParts[1]
            [void]$sql.AppendLine((Add-WaaDriverSql -Code $driverCode -Name $driverName))

            $driverCodeSql = ConvertTo-WaaSqlLiteral $driverCode
            $truckSql = ConvertTo-WaaSqlLiteral $row[1]
            $startSql = ConvertTo-WaaSqlLiteral (Parse-Date $row[3])
            $endSql = ConvertTo-WaaSqlLiteral (Parse-Date $row[2])

            $engineHours = 0.0
            $idleHours = 0.0
            $engineOk = [double]::TryParse(
                [string]$row[4],
                [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$engineHours
            )
            $idleOk = [double]::TryParse(
                [string]$row[5],
                [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture,
                [ref]$idleHours
            )
            if (-not $engineOk) { throw "Invalid engine hours on row $($rowIndex + 1)" }
            if (-not $idleOk) { throw "Invalid idle hours on row $($rowIndex + 1)" }

            $engineSql = ConvertTo-WaaSqlLiteral $engineHours
            $idleSql = ConvertTo-WaaSqlLiteral $idleHours

            [void]$sql.AppendLine(
                "INSERT INTO idle_periods(driver_id,truck,period_start,period_end,engine_hours,idle_hours,import_batch_id) " +
                "SELECT driver_id,$truckSql,$startSql,$endSql,$engineSql,$idleSql,(SELECT id FROM import_batches WHERE source_hash=$hashSql) " +
                "FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$driverCodeSql COLLATE NOCASE LIMIT 1 " +
                "ON CONFLICT(driver_id,period_start,period_end) DO UPDATE SET " +
                "truck=excluded.truck,engine_hours=excluded.engine_hours,idle_hours=excluded.idle_hours,import_batch_id=excluded.import_batch_id;"
            )
        }
    }
    else {
        for ($rowIndex = 1; $rowIndex -lt $rows.Count; $rowIndex++) {
            $row = @($rows[$rowIndex])
            if ($row.Count -lt 8) { continue }

            $order = [string]$row[0]
            $driverCode = [string]$row[6]
            $driverName = [string]$row[7]
            if ([string]::IsNullOrWhiteSpace($order) -and [string]::IsNullOrWhiteSpace($driverCode)) { continue }

            [void]$sql.AppendLine((Add-WaaDriverSql -Code $driverCode -Name $driverName))

            $orderSql = ConvertTo-WaaSqlLiteral $order
            $driverCodeSql = ConvertTo-WaaSqlLiteral $driverCode
            $dateSql = ConvertTo-WaaSqlLiteral (Parse-Date $row[1])
            $originSql = ConvertTo-WaaSqlLiteral $row[2]
            $destinationSql = ConvertTo-WaaSqlLiteral $row[3]
            $mileageSql = ConvertTo-WaaSqlLiteral $row[4]
            $bolTypeSql = ConvertTo-WaaSqlLiteral $row[5]
            $rowJsonSql = ConvertTo-WaaSqlLiteral ($row | ConvertTo-Json -Compress)
            $driverExpression = "(SELECT driver_id FROM driver_aliases WHERE alias_type='dispatch_code' AND alias_value=$driverCodeSql COLLATE NOCASE LIMIT 1)"

            [void]$sql.AppendLine(
                "UPDATE missing_bols SET empty_call_date=$dateSql,origin=$originSql,destination=$destinationSql," +
                "mileage=$mileageSql,bol_type=$bolTypeSql,raw_fields_json=$rowJsonSql,last_seen_at=CURRENT_TIMESTAMP," +
                "import_batch_id=(SELECT id FROM import_batches WHERE source_hash=$hashSql) " +
                "WHERE order_number=$orderSql AND driver_id=$driverExpression;"
            )
            [void]$sql.AppendLine(
                "INSERT INTO missing_bols(driver_id,order_number,empty_call_date,origin,destination,mileage,bol_type,raw_fields_json,import_batch_id) " +
                "SELECT $driverExpression,$orderSql,$dateSql,$originSql,$destinationSql,$mileageSql,$bolTypeSql,$rowJsonSql," +
                "(SELECT id FROM import_batches WHERE source_hash=$hashSql) " +
                "WHERE $driverExpression IS NOT NULL " +
                "AND NOT EXISTS(SELECT 1 FROM missing_bols WHERE order_number=$orderSql AND driver_id=$driverExpression);"
            )
        }
    }

    $auditJson = @{ type = $Type; file = $Filename } | ConvertTo-Json -Compress
    $auditSql = ConvertTo-WaaSqlLiteral $auditJson
    [void]$sql.AppendLine(
        "INSERT INTO audit_history(action,entity_type,entity_id,detail_json) VALUES(" +
        "'downloads_import','import_batch',(SELECT id FROM import_batches WHERE source_hash=$hashSql),$auditSql);"
    )
    [void]$sql.AppendLine('COMMIT;')

    try {
        Invoke-Sql $sql.ToString() -AllowWrite | Out-Null
    }
    catch {
        try { Invoke-Sql 'ROLLBACK;' -AllowWrite | Out-Null } catch { }
        throw
    }

    $batch = @(Invoke-Sql "SELECT id FROM import_batches WHERE source_hash=$hashSql;" -Json)
    return @{
        status = 'Imported'
        imported = $true
        import_batch_id = [int]$batch[0].id
        hash = $hash
        detail = "$sourceRowCount source rows processed."
    }
}

function Set-WaaIntakeStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Downloads,
        [AllowNull()]$File,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Detail,
        [AllowNull()]$Managed,
        [AllowNull()]$Hash,
        [AllowNull()]$ImportId
    )

    $name = if ($null -ne $File) { $File.Name } else { $null }
    $path = if ($null -ne $File) { $File.FullName } else { $null }
    $modified = if ($null -ne $File) { $File.LastWriteTimeUtc.ToString('s') } else { $null }

    $values = @(
        $Downloads, $name, $path, $modified, $Hash, $Managed, $ImportId, $Status, $Detail, $Type
    ) | ForEach-Object { ConvertTo-WaaSqlLiteral $_ }

    $sql = @"
INSERT INTO report_intake_status(
  report_type,downloads_path,source_name,source_path,source_modified_utc,source_hash,
  managed_path,import_batch_id,status,detail,scanned_at
)
VALUES(
  $($values[9]),$($values[0]),$($values[1]),$($values[2]),$($values[3]),$($values[4]),
  $($values[5]),$($values[6]),$($values[7]),$($values[8]),CURRENT_TIMESTAMP
)
ON CONFLICT(report_type) DO UPDATE SET
  downloads_path=excluded.downloads_path,
  source_name=excluded.source_name,
  source_path=excluded.source_path,
  source_modified_utc=excluded.source_modified_utc,
  source_hash=excluded.source_hash,
  managed_path=excluded.managed_path,
  import_batch_id=excluded.import_batch_id,
  status=excluded.status,
  detail=excluded.detail,
  scanned_at=CURRENT_TIMESTAMP;
"@
    Invoke-Sql $sql -AllowWrite | Out-Null
}

function Get-WaaNewestDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Downloads,
        [Parameter(Mandatory = $true)][ValidateSet('idle', 'bol')][string]$Type
    )

    if (-not (Test-Path -LiteralPath $Downloads)) { return $null }

    $namePattern = if ($Type -eq 'bol') {
        '(?i)(missing.*bol|bol.*missing|order.*details.*bol)'
    }
    else {
        '(?i)(rolling\s*7|rolling7|7\s*day.*idle|idle.*7\s*day)'
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $Downloads -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Extension.ToLowerInvariant() -in @('.xlsx', '.csv', '.txt') -and
                $_.BaseName -match $namePattern
            } |
            Sort-Object LastWriteTimeUtc -Descending
    )

    if ($candidates.Count -eq 0) { return $null }
    return $candidates[0]
}

function Invoke-WaaDownloadsScan {
    param([string]$DownloadsPath)

    Initialize-WaaReportIntake
    $downloads = if (-not [string]::IsNullOrWhiteSpace($DownloadsPath)) {
        $DownloadsPath
    }
    else {
        Get-WaaDownloadsPath
    }

    $reportRoot = Get-WaaReportRoot
    $results = @{}

    foreach ($type in @('idle', 'bol')) {
        $file = Get-WaaNewestDownload -Downloads $downloads -Type $type
        if ($null -eq $file) {
            Set-WaaIntakeStatus -Type $type -Downloads $downloads -File $null -Status 'Waiting' -Detail 'No matching report found in Downloads.' -Managed $null -Hash $null -ImportId $null
            $results[$type] = @{ status = 'Waiting'; detail = 'No matching report found.' }
            continue
        }

        try {
            $fileHash = Get-WaaFileSha256 -Path $file.FullName
            $typeSql = ConvertTo-WaaSqlLiteral $type
            $previous = @(Invoke-Sql "SELECT source_hash,status,managed_path,import_batch_id FROM report_intake_status WHERE report_type=$typeSql;" -Json)

            if ($previous.Count -gt 0 -and
                $previous[0].source_hash -eq $fileHash -and
                $previous[0].status -in @('Imported', 'Current')) {
                $results[$type] = @{
                    status = 'Current'
                    file = $file.Name
                    imported = $false
                    detail = 'Newest report is already current.'
                }
                continue
            }

            $canonical = Get-WaaCanonicalReportText -Path $file.FullName -Type $type
            $folderName = if ($type -eq 'bol') { 'missing-bol' } else { 'idle' }
            $folder = Join-Path $reportRoot $folderName
            $stamp = $file.LastWriteTimeUtc.ToString('yyyyMMdd-HHmmss')
            $managedPath = Join-Path $folder ($stamp + '_' + $file.Name)

            if (-not (Test-Path -LiteralPath $managedPath)) {
                Copy-Item -LiteralPath $file.FullName -Destination $managedPath -Force
            }

            $import = Import-WaaManagedReport -Canonical $canonical -Filename $file.Name -Type $type
            Set-WaaIntakeStatus -Type $type -Downloads $downloads -File $file -Status $import.status -Detail $import.detail -Managed $managedPath -Hash $fileHash -ImportId $import.import_batch_id

            $results[$type] = @{
                status = $import.status
                file = $file.Name
                managed = $managedPath
                imported = $import.imported
                detail = $import.detail
            }
        }
        catch {
            $errorHash = $null
            try { $errorHash = Get-WaaFileSha256 -Path $file.FullName } catch { }

            Set-WaaIntakeStatus -Type $type -Downloads $downloads -File $file -Status 'Error' -Detail $_.Exception.Message -Managed $null -Hash $errorHash -ImportId $null
            $results[$type] = @{
                status = 'Error'
                file = $file.Name
                detail = $_.Exception.Message
            }
        }
    }

    return @{
        downloads_path = $downloads
        results = $results
        scanned_at = (Get-Date).ToUniversalTime().ToString('s')
    }
}

function Get-WaaReportIntakeStatus {
    Initialize-WaaReportIntake
    $rows = @(Invoke-Sql 'SELECT * FROM report_intake_status ORDER BY report_type;' -Json)
    $map = @{}
    foreach ($row in $rows) {
        $map[[string]$row.report_type] = $row
    }

    return @{
        downloads_path = Get-WaaDownloadsPath
        reports_root = Get-WaaReportRoot
        idle = $map['idle']
        bol = $map['bol']
    }
}
