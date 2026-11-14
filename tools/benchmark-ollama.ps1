param([string]$Model = "qwen3:8b", [string]$EmbedModel = "nomic-embed-text")
$ErrorActionPreference = "Stop"
$base = "http://localhost:11434"
function Post($path, $body) {
    $json = $body | ConvertTo-Json -Depth 20 -Compress
    Invoke-RestMethod -Uri "$base$path" -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($json)) -ContentType "application/json; charset=utf-8" -TimeoutSec 120
}
function Stats($r, $label) {
    $gen = [math]::Round($r.eval_count / ($r.eval_duration / 1e9), 1)
    "[$label] output=$($r.eval_count) tok @ $gen tok/s  total=$([math]::Round($r.total_duration / 1e9, 2))s"
}

"=== 4b. NL -> filter (model-friendly schema: relative dates + integer hours, code does the math) ==="
$schema = @{
    type = "object"
    properties = @{
        offense_types   = @{ type="array"; maxItems=4; items=@{ type="string"; enum=@("BURGLARY","ROBBERY","THEFT","THEFT_FROM_VEHICLE","AUTO_THEFT","ASSAULT","VANDALISM","NARCOTICS","DOMESTIC") } }
        districts       = @{ type="array"; maxItems=4; items=@{ type="string"; enum=@("NORTH","SOUTH","EAST","WEST") } }
        lookback_months = @{ type="integer" }
        since_month     = @{ type="integer" }
        hour_from       = @{ type="integer" }
        hour_to         = @{ type="integer" }
        premise_types   = @{ type="array"; maxItems=4; items=@{ type="string"; enum=@("RESIDENTIAL","COMMERCIAL","VEHICLE","PUBLIC","SCHOOL") } }
        mo_tags         = @{ type="array"; maxItems=4; items=@{ type="string"; enum=@("FORCED_ENTRY","PRY_TOOL","WINDOW_SMASH","REAR_ENTRY","ALARM_DEFEATED","WEAPON_KNIFE","WEAPON_FIREARM") } }
        semantic_query  = @{ type="string" }
    }
    required = @("offense_types","districts","lookback_months","since_month","hour_from","hour_to","premise_types","mo_tags","semantic_query")
}
$sys = @"
You convert law-enforcement search questions into a JSON search filter. Use only allowed enum values. Leave arrays empty when not mentioned.
Rules:
- lookback_months: number of months back if the user gives a relative period ("last 18 months" = 18, "past year" = 12, "this year" = 0). 0 if none.
- since_month: calendar month number 1-12 if the user says "since <month>" or "this year" (this year = 1). 0 if none.
- hour_from / hour_to: 24-hour clock integers. "after 10pm" = hour_from 22, hour_to 6 (overnight). "before 6am" = hour_from 0, hour_to 6. -1 for both if no time mentioned.
- semantic_query: 3-8 words describing the M.O. for similarity search.
Example: "residential burglaries in west last 6 months between 1am and 5am with a pry bar" ->
{"offense_types":["BURGLARY"],"districts":["WEST"],"lookback_months":6,"since_month":0,"hour_from":1,"hour_to":5,"premise_types":["RESIDENTIAL"],"mo_tags":["PRY_TOOL","FORCED_ENTRY"],"semantic_query":"pry bar forced entry home overnight"}
"@
$today = [datetime]"2026-11-14"
$questions = @(
  "Show similar burglaries in the north district last 18 months involving forced entry on commercial buildings after 10pm",
  "any car break-ins with smashed windows in east or west since june",
  "knife assaults in the south district this year",
  "commercial burglaries where the alarm was defeated, past 2 years"
)
foreach ($q in $questions) {
    $r = Post "/api/chat" @{ model=$Model; stream=$false; think=$false; format=$schema; keep_alive="30m"; options=@{ temperature=0; num_predict=300 }
        messages=@(@{ role="system"; content=$sys }, @{ role="user"; content=$q }) }
    "Q: $q"
    Stats $r "filter"
    try {
        $f = $r.message.content | ConvertFrom-Json
        $f | ConvertTo-Json -Compress -Depth 5
        $from = if ($f.lookback_months -gt 0) { $today.AddMonths(-$f.lookback_months) } elseif ($f.since_month -gt 0) { $y = if ($f.since_month -le $today.Month) { $today.Year } else { $today.Year - 1 }; [datetime]::new($y, $f.since_month, 1) } else { $null }
        $hrs = if ($f.hour_from -ge 0) { "{0:00}:00-{1:00}:00" -f $f.hour_from, $f.hour_to } else { "any time" }
        "  => code-computed: from=$(if($from){$from.ToString('yyyy-MM-dd')}else{'(no limit)'}) to=2026-11-14, hours=$hrs"
    } catch { "INVALID JSON: $($r.message.content)" }
    ""
}

"=== 5. Grounded answer with citations ==="
$ctx = @"
[Policy 300.3.2] Deputies may use deadly force when they reasonably believe it is necessary to protect themselves or others from what they reasonably believe is an imminent threat of death or serious bodily injury.
[Policy 300.3.4] When feasible, deputies should use de-escalation techniques, create distance, use cover, and request additional resources before using force, especially when a subject is armed with an edged weapon.
[Policy 300.5.1] Any use of force must be reported to a supervisor before end of shift and documented in a Use of Force report.
"@
$r = Post "/api/chat" @{ model=$Model; stream=$false; think=$false; keep_alive="30m"; options=@{ temperature=0.1; num_predict=500 }
    messages=@(
      @{ role="system"; content="Answer ONLY from the provided policy excerpts. Cite the section number in brackets after each claim, e.g. [Policy 300.3.2]. If the excerpts do not answer part of the question, say clearly that the provided policy does not cover it." },
      @{ role="user"; content="POLICY EXCERPTS:`n$ctx`n`nQUESTION: What does policy say about use of force with a knife threat when the subject is non-compliant? Also, what is the policy on vehicle pursuits?" }) }
Stats $r "rag"
$r.message.content

"`n=== 6. Embeddings ==="
$texts = 1..32 | ForEach-Object { "Unknown suspect forced the rear door of the commercial business using a pry tool between 2200 and 0400 hours. Case $_." }
$t = [Diagnostics.Stopwatch]::StartNew(); $e = Post "/api/embed" @{ model=$EmbedModel; input=$texts; keep_alive="30m" }; $t.Stop()
"Cold: $($e.embeddings.Count) chunks, dim=$($e.embeddings[0].Count), $([math]::Round($t.Elapsed.TotalSeconds,2))s"
$t = [Diagnostics.Stopwatch]::StartNew(); $e = Post "/api/embed" @{ model=$EmbedModel; input=$texts }; $t.Stop()
"Warm: 32 chunks in $([math]::Round($t.Elapsed.TotalSeconds,2))s => ~$([math]::Round(32/$t.Elapsed.TotalSeconds,0)) chunks/s"

"`n=== 7. Both models resident ==="
foreach ($m in (Invoke-RestMethod "$base/api/ps").models) { "{0}: VRAM {1:N2} GB of {2:N2} GB" -f $m.name, ($m.size_vram/1GB), ($m.size/1GB) }
nvidia-smi --query-gpu=memory.used,memory.total --format=csv

"`n=== 8. Reload time (unload, then load again from disk cache) ==="
$null = Post "/api/generate" @{ model=$Model; keep_alive=0 }
Start-Sleep -Seconds 2
$r = Post "/api/chat" @{ model=$Model; stream=$false; think=$false; options=@{ num_predict=5 }; messages=@(@{ role="user"; content="Say OK." }) }
"Reload: load=$([math]::Round($r.load_duration/1e9,2))s"

