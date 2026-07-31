<#
.SYNOPSIS
  Watches plans/*.md status and automatically hands work between Codex (implementer)
  and Claude Code (reviewer), per CLAUDE.md / AGENTS.md. Never flips a spec to DONE
  itself -- that stays a manual, human step.

.DESCRIPTION
  Each iteration:
    - If no spec is IN_PROGRESS or BLOCKED, dispatches `codex exec` on the lowest-numbered
      READY spec (workspace-write sandbox: Codex can edit files and run build/test commands
      inside this repo, nothing outside it).
    - For every spec at REVIEW whose content hash has changed since it was last auto-reviewed,
      dispatches `claude -p` to run the CLAUDE.md review checklist. If clean, it appends a
      "Recommend: DONE -- awaiting owner approval." line and leaves status at REVIEW. If not
      clean, it writes actionable review notes and flips status back to IN_PROGRESS so Codex
      picks it up next cycle.
    - A spec at BLOCKED is left alone -- that needs a human decision on its Open Questions.

  Nothing in this loop pushes, commits, or merges. Flipping a spec to DONE is left for you
  (or a future Claude/Codex turn) to do explicitly after glancing at the "Recommend: DONE" note.

.PARAMETER IntervalSeconds
  Delay between polling cycles. Default 600 (10 min).

.PARAMETER MaxIterations
  Stop after this many cycles. Default 0 (run until Ctrl+C). Use 1 for a first dry run.

.EXAMPLE
  # Sanity-check the whole pipeline once before leaving it running unattended.
  .\scripts\agent-loop.ps1 -MaxIterations 1

.EXAMPLE
  # Leave it running in a visible terminal window; Ctrl+C to stop.
  .\scripts\agent-loop.ps1
#>

param(
    [int]$IntervalSeconds = 600,
    [int]$MaxIterations = 0
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$PlansDir = Join-Path $RepoRoot "plans"
$LogDir = Join-Path $RepoRoot "scripts\agent-loop-logs"
$StateFile = Join-Path $RepoRoot "scripts\.agent-loop-state.json"

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

function Get-SpecStatus {
    param([string]$Path)
    $line = Get-Content -Path $Path -TotalCount 6 | Where-Object { $_ -match '^status:\s*(\S+)' }
    if ($line -and $line -match '^status:\s*(\S+)') { return $Matches[1] }
    return $null
}

function Load-State {
    # Windows PowerShell 5.1 has no -AsHashtable on ConvertFrom-Json; convert manually.
    if (Test-Path $StateFile) {
        $raw = Get-Content -Path $StateFile -Raw
        if ($raw) {
            $obj = $raw | ConvertFrom-Json
            $ht = @{}
            foreach ($prop in $obj.PSObject.Properties) { $ht[$prop.Name] = $prop.Value }
            return $ht
        }
    }
    return @{}
}

function Save-State {
    param($State)
    ($State | ConvertTo-Json -Depth 5) | Set-Content -Path $StateFile -Encoding utf8
}

function Write-Log {
    param([string]$Message, [string]$LogPath)
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line
}

$CodexPrompt = @'
Read AGENTS.md and docs/CONVENTIONS.md in full, then implement {SPEC} exactly per its
Acceptance Criteria and Codex handoff section. Follow AGENTS.md's rules exactly:
flip status to IN_PROGRESS when you start; build only what Acceptance Criteria asks for;
if you hit an ambiguous decision, add it under Open Questions and flip status to BLOCKED
rather than guessing; when done, flip status to REVIEW and write a `## What shipped` note.
'@

$ClaudeReviewPrompt = @'
Act as the reviewer role defined in CLAUDE.md. Read CLAUDE.md's review checklist in full,
then review {SPEC}, currently at status REVIEW. Verify the diff yourself (git diff, and read
any new/untracked files it introduced) against every item in the spec's Acceptance Criteria --
do not just trust the spec's own "What shipped" section. Check docs/CONVENTIONS.md compliance,
confirm every edge case in the spec is actually handled, and flag any leftover stub/TODO or
scope creep. This is a fully unattended automated pass with no human present to answer
questions -- do not ask the user anything, do not wait for confirmation, make your own call
using the spec, CLAUDE.md, and the code as your only sources of truth.

Only ever edit {SPEC} itself. Never edit application source or test code -- if something needs
a code change, that is Codex's job on a future pass, not yours.

If everything genuinely checks out: append a `## Review notes` section (or add to an existing
one) summarizing what you verified, and end it with the literal line
`Recommend: DONE -- awaiting owner approval.` Do NOT change the status field in this case --
leave it at REVIEW; a human will flip it to DONE after a final look.

If it is not clean: add specific, actionable notes under `## Review notes` describing exactly
what is missing or wrong, and change the status field to IN_PROGRESS so Codex addresses it on
the next cycle.
'@

$iteration = 0
while ($true) {
    $iteration++
    $timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
    $log = Join-Path $LogDir "$timestamp.log"
    Write-Log "=== Iteration $iteration ===" $log

    $specs = Get-ChildItem -Path $PlansDir -Filter "*.md" |
        Where-Object { $_.Name -match '^\d{3}-' -and $_.Name -ne "000-roadmap.md" } |
        Sort-Object Name

    $statuses = [ordered]@{}
    foreach ($s in $specs) { $statuses[$s.Name] = Get-SpecStatus $s.FullName }

    $blockedNames = $statuses.Keys | Where-Object { $statuses[$_] -eq "BLOCKED" }
    $inProgressNames = $statuses.Keys | Where-Object { $statuses[$_] -eq "IN_PROGRESS" }

    if ($blockedNames) {
        Write-Log "BLOCKED: $($blockedNames -join ', ') -- needs a human decision on Open Questions. Not dispatching Codex this cycle." $log
    }
    elseif ($inProgressNames) {
        Write-Log "Already IN_PROGRESS: $($inProgressNames -join ', ') -- not starting another (one spec at a time)." $log
    }
    else {
        $readyName = $statuses.Keys | Where-Object { $statuses[$_] -eq "READY" } | Select-Object -First 1
        if ($readyName) {
            Write-Log "Dispatching Codex on $readyName" $log
            $prompt = $CodexPrompt.Replace("{SPEC}", "plans/$readyName")
            & codex exec -C $RepoRoot -s workspace-write $prompt *>> $log
            Write-Log "Codex run finished for $readyName (exit code $LASTEXITCODE)" $log
        }
        else {
            Write-Log "No READY spec waiting." $log
        }
    }

    $state = Load-State
    $reviewSpecs = $specs | Where-Object { $statuses[$_.Name] -eq "REVIEW" }
    foreach ($spec in $reviewSpecs) {
        $hash = (Get-FileHash -Path $spec.FullName -Algorithm SHA256).Hash
        if ($state[$spec.Name] -eq $hash) {
            Write-Log "Skipping review of $($spec.Name) -- unchanged since last auto-review, still awaiting owner approval." $log
            continue
        }
        Write-Log "Dispatching Claude review on $($spec.Name)" $log
        $prompt = $ClaudeReviewPrompt.Replace("{SPEC}", "plans/$($spec.Name)")
        Push-Location $RepoRoot
        try {
            & claude -p --permission-mode acceptEdits `
                --allowedTools "Read,Grep,Glob,Edit,Bash(dotnet build:*),Bash(dotnet test:*),Bash(git diff:*),Bash(git status:*),Bash(git log:*)" `
                $prompt *>> $log
        }
        finally {
            Pop-Location
        }
        Write-Log "Claude review finished for $($spec.Name) (exit code $LASTEXITCODE)" $log

        $newHash = (Get-FileHash -Path $spec.FullName -Algorithm SHA256).Hash
        $state[$spec.Name] = $newHash
        Save-State $state
    }

    if ($MaxIterations -gt 0 -and $iteration -ge $MaxIterations) {
        Write-Log "Reached MaxIterations ($MaxIterations), stopping." $log
        break
    }
    Write-Log "Sleeping $IntervalSeconds seconds..." $log
    Start-Sleep -Seconds $IntervalSeconds
}
