param(
    [Parameter(Mandatory = $false)]
    [string] $TrxPath = '.\TestResults\test-results.trx',

    [Parameter(Mandatory = $false)]
    [string] $OutDir = '.\TestResults'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TrxPath)) {
    throw "TRX file not found: $TrxPath"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

[xml]$xml = Get-Content -LiteralPath $TrxPath

$nsUri = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
$ns.AddNamespace('t', $nsUri)

$unitResults = @(
    $xml.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns) |
        ForEach-Object {
            $dur = [TimeSpan]::Zero
            $durAttr = $_.GetAttribute('duration')
            if ($durAttr) { $dur = [TimeSpan]::Parse($durAttr) }

            [pscustomobject]@{
                TestName = $_.GetAttribute('testName')
                Outcome  = $_.GetAttribute('outcome')
                Duration = $dur
            }
        }
)

$testCases = @(
    [pscustomobject]@{ Id = 'SF-01';  Area = 'Score File';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.ProgramWriteScoreFileAtomicTests.WriteScoreFileAtomic_WritesJson_WithoutCode_WhenNull' }
    [pscustomobject]@{ Id = 'SF-02';  Area = 'Score File';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.ProgramWriteScoreFileAtomicTests.WriteScoreFileAtomic_WritesJson_WithCode_WhenProvided' }

    [pscustomobject]@{ Id = 'EDU-01'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.Initialize_ResetsState' }
    [pscustomobject]@{ Id = 'EDU-02'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.HandleInput_MovesBucketWithinBounds' }
    [pscustomobject]@{ Id = 'EDU-03'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.Update_BlockCaught_IncrementsScore' }
    [pscustomobject]@{ Id = 'EDU-04'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.Update_BlockMissed_DecrementsLives_AndSetsGameOver_WhenLivesReachZero' }
    [pscustomobject]@{ Id = 'EDU-05'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.HandleInput_WhenGameOver_EscapeSetsStateChanged' }
    [pscustomobject]@{ Id = 'EDU-06'; Area = 'Education';     Mode = 'Exact';  Match = 'ConsoleTest.Tests.EducationTests.SetGameOverCode_SetsAndReturns' }

    [pscustomobject]@{ Id = 'GEN-01'; Area = 'Generic Games'; Mode = 'Prefix'; Match = 'ConsoleTest.Tests.GenericGameTests.Game_Lifecycle_NoExceptions' }

    [pscustomobject]@{ Id = 'API-01'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.SubmitCodeAsync_ReturnsTrue_On200' }
    [pscustomobject]@{ Id = 'API-02'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.SubmitCodeAsync_ReturnsFalse_OnServerError' }
    [pscustomobject]@{ Id = 'API-03'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.SubmitCodeAsync_Throws_OnMissingCode' }
    [pscustomobject]@{ Id = 'API-04'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.SubmitCodeAsync_Throws_OnMissingGameCode' }
    [pscustomobject]@{ Id = 'API-05'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.SubmitCodeAsync_SetsApiKeyHeader_WhenProvided' }
    [pscustomobject]@{ Id = 'API-06'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.GetTopScoresAsync_ParsesJson_On200' }
    [pscustomobject]@{ Id = 'API-07'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.GetTopScoresAsync_ReturnsEmpty_OnNonSuccess' }
    [pscustomobject]@{ Id = 'API-08'; Area = 'API Client';    Mode = 'Exact';  Match = 'ConsoleTest.Tests.PixelCatsApiClientTests.GetTopScoresAsync_ReturnsEmpty_OnInvalidJson' }
)

function Select-MatchedResults {
    param(
        [Parameter(Mandatory = $true)][string] $Mode,
        [Parameter(Mandatory = $true)][string] $Match
    )

    if ($Mode -eq 'Exact')  { return @($unitResults | Where-Object { $_.TestName -eq $Match }) }
    if ($Mode -eq 'Prefix') { return @($unitResults | Where-Object { $_.TestName -like ($Match + '*') }) }

    throw "Unknown match mode: $Mode"
}

$rows = foreach ($tc in $testCases) {
    $matched = @(Select-MatchedResults -Mode $tc.Mode -Match $tc.Match)

    $count = $matched.Count
    $passed = @($matched | Where-Object { $_.Outcome -eq 'Passed' }).Count
    $failed = @($matched | Where-Object { $_.Outcome -ne 'Passed' }).Count

    # Windows PowerShell 5.1 can't sum TimeSpan with Measure-Object, so sum ticks manually.
    $durationTicks = 0L
    foreach ($m in $matched) {
        if ($null -ne $m.Duration) { $durationTicks += $m.Duration.Ticks }
    }
    $duration = [TimeSpan]::FromTicks($durationTicks)

    $outcome =
        if ($count -eq 0) { 'NotFound' }
        elseif ($failed -gt 0) { 'Failed' }
        else { 'Passed' }

    $details =
        if ($count -eq 0) { '0/0 matched' }
        else { "$passed/$count passed" }

    [pscustomobject]@{
        Id       = $tc.Id
        Area     = $tc.Area
        TestName = $tc.Match
        Outcome  = $outcome
        Details  = $details
        Duration = $duration
    }
}

$resolvedTrx = (Resolve-Path -LiteralPath $TrxPath).Path

$mdPath = Join-Path $OutDir 'test-case-results.md'
$md = New-Object System.Collections.Generic.List[string]
$md.Add('# Test Case Results')
$md.Add('')
$md.Add("> Source: `"$resolvedTrx`"")
$md.Add('')
$md.Add('| ID | Area | Test (mapping) | Outcome | Details | Duration |')
$md.Add('|---:|------|----------------|---------|---------|----------|')

foreach ($r in $rows) {
    $md.Add(('| {0} | {1} | `{2}` | {3} | {4} | {5} |' -f $r.Id, $r.Area, $r.TestName, $r.Outcome, $r.Details, $r.Duration))
}

$md | Set-Content -Encoding UTF8 -LiteralPath $mdPath

$htmlPath = Join-Path $OutDir 'test-case-results.html'
$trRows = ($rows | ForEach-Object {
    $cls = if ($_.Outcome -eq 'Passed') { 'ok' } elseif ($_.Outcome -eq 'Failed') { 'bad' } else { 'warn' }
    "<tr class='$cls'><td>$($_.Id)</td><td>$($_.Area)</td><td><code>$($_.TestName)</code></td><td>$($_.Outcome)</td><td>$($_.Details)</td><td>$($_.Duration)</td></tr>"
}) -join "`n"

$html = @"
<!doctype html>
<html>
<head>
  <meta charset="utf-8" />
  <title>Test Case Results</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; }
    table { border-collapse: collapse; width: 100%; margin: 12px 0; }
    th, td { border: 1px solid #ddd; padding: 8px; vertical-align: top; }
    th { background: #f5f5f5; text-align: left; }
    .ok td:nth-child(4) { color: #107c10; font-weight: 600; }
    .bad td:nth-child(4) { color: #a80000; font-weight: 600; }
    .warn td:nth-child(4) { color: #8a6d3b; font-weight: 600; }
    code { white-space: nowrap; }
  </style>
</head>
<body>
  <h1>Test Case Results</h1>
  <p><b>Source:</b> $resolvedTrx</p>
  <table>
    <tr>
      <th>ID</th><th>Area</th><th>Test (mapping)</th><th>Outcome</th><th>Details</th><th>Duration</th>
    </tr>
    $trRows
  </table>
</body>
</html>
"@

Set-Content -Encoding UTF8 -LiteralPath $htmlPath -Value $html

Write-Host 'Wrote:'
Write-Host " - $mdPath"
Write-Host " - $htmlPath"