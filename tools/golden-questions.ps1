# Runs the demo "golden questions" against a running BLADE server and prints what the UI would show.
# Usage:  .\tools\golden-questions.ps1 [-Base http://localhost:5179] [-Role ANALYST] [-Only ask|policy|security]
param([string]$Base = "http://localhost:5179", [string]$Role = "ANALYST", [string]$Only = "")
$ErrorActionPreference = "Stop"
$headers = @{ "X-Blade-User" = "demo.analyst"; "X-Blade-Role" = $Role; "X-Blade-Workstation" = "DEMO-LAPTOP" }

function Post($path, $body, $h = $headers) {
    $json = $body | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod -Uri "$Base$path" -Method Post -Headers $h -Body ([Text.Encoding]::UTF8.GetBytes($json)) -ContentType "application/json; charset=utf-8" -TimeoutSec 300
}
function Stages($t) { ($t.stages | ForEach-Object { "{0} {1}ms" -f $_.name.Split(' (')[0], $_.ms }) -join " | " }

if (-not $Only -or $Only -eq "ask") {
  foreach ($q in @(
    "Show similar burglaries in the north district last 18 months involving forced entry on commercial buildings after 10pm",
    "Any catalytic converter thefts in the east or south districts this year?"
  )) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Post "/api/ask" @{ question = $q }
    "`n================ ASK: $q  ($([math]::Round($sw.Elapsed.TotalSeconds,1)) s)"
    "FILTER: " + (($r.chips | ForEach-Object { "$($_.label)=$($_.value)" }) -join " | ")
    "MATCHES: $($r.totalMatches)  withheld: $($r.withheld)  warnings: $($r.warnings -join '; ')"
    if ($r.series) { "SERIES: $($r.series.summary)"; $r.series.members | ForEach-Object { "   - $($_.caseNumber): $($_.reason)" } }
    "LINKS:"; $r.links | Select-Object -First 6 | ForEach-Object { "   [$($_.kind)] $($_.summary)" }
    "CITATIONS: " + (($r.citations | ForEach-Object { "$($_.id)$(if (-not $_.verified) {'(UNVERIFIED)'})" }) -join ", ")
    "STAGES: " + (Stages $r.trace) + "  tokens: $($r.trace.promptTokens)/$($r.trace.completionTokens)"
    "--- ANSWER ---"; $r.answer
  }
}

if (-not $Only -or $Only -eq "policy") {
  foreach ($q in @(
    "What does policy say about use of force with a knife threat when the subject is non-compliant?",
    "Can I use AI to help write my report narrative?",
    "What is our policy on vehicle pursuits for a stolen car?"
  )) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $r = Post "/api/policy" @{ question = $q }
    "`n================ POLICY: $q  ($([math]::Round($sw.Elapsed.TotalSeconds,1)) s)"
    "SECTIONS: " + (($r.sections | ForEach-Object { $_.sectionNumber }) -join ", ") + "   related cases: " + (($r.relatedCases | ForEach-Object { $_.caseNumber }) -join ", ")
    "CITATIONS: " + (($r.citations | ForEach-Object { "$($_.id)$(if (-not $_.verified) {'(UNVERIFIED)'})" }) -join ", ") + "  warnings: $($r.warnings -join '; ')"
    "STAGES: " + (Stages $r.trace)
    "--- ANSWER ---"; $r.answer
  }
}

if (-not $Only -or $Only -eq "security") {
  "`n================ SECURITY CHECKS"
  $dep = @{ "X-Blade-User" = "demo.deputy"; "X-Blade-Role" = "DEPUTY"; "X-Blade-Workstation" = "MDT-114" }
  try { Invoke-RestMethod "$Base/api/audit?take=5" -Headers $dep | Out-Null; "FAIL: deputy could read audit log" } catch { "OK: deputy denied audit log ($($_.Exception.Response.StatusCode.value__))" }
  $sup = @{ "X-Blade-User" = "sgt.review"; "X-Blade-Role" = "SUPERVISOR"; "X-Blade-Workstation" = "HQ-22" }
  $v = Invoke-RestMethod "$Base/api/audit/verify" -Headers $sup
  "AUDIT CHAIN: $($v.message)"
  "USAGE:"; Invoke-RestMethod "$Base/api/audit/usage" -Headers $sup | Format-Table user, role, aiQueries, recordViews, denied, tokens -AutoSize | Out-String
}
