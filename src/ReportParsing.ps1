Set-StrictMode -Version Latest

function Parse-Date([string]$Text){
  if([string]::IsNullOrWhiteSpace($Text)){return $null}
  $n=0.0
  if([double]::TryParse($Text,[Globalization.NumberStyles]::Float,[Globalization.CultureInfo]::InvariantCulture,[ref]$n) -and $n -ge 20000 -and $n -le 100000){
    try{return [datetime]::FromOADate($n).ToString('s')}catch{}
  }
  $d=[datetime]::MinValue;$styles=[Globalization.DateTimeStyles]::AssumeLocal
  if([datetime]::TryParse($Text,[Globalization.CultureInfo]::InvariantCulture,$styles,[ref]$d)){return $d.ToString('s')}
  return $null
}

function Split-ImportRows([string]$Raw){
  $lines=@($Raw -split "`r?`n" | Where-Object {$_.Trim()});$result=@()
  foreach($line in $lines){
    if($line.Contains("`t")){$cells=@([regex]::Split($line,"`t"))}
    elseif($line.Trim().StartsWith('|')){$cells=@($line.Trim().Trim('|') -split '(?<!\\)\|' | ForEach-Object {$_.Trim().Replace('\_','_').Replace('\|','|')})}
    else{$cells=@($line -split ',(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)' | ForEach-Object {$_.Trim(' ','"')})}
    if(($cells -join '') -match '^[-: ]+$'){continue}
    $result += ,$cells
  }
  Write-Output -NoEnumerate $result
}
