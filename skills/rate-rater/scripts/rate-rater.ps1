param(
  [ValidateSet('start', 'note', 'block', 'finish', 'hourly', 'install')]
  [string]$Mode = 'hourly',
  [string]$TaskId,
  [string]$Title,
  [string]$Scope,
  [string]$Remaining5h,
  [string]$RemainingWeekly,
  [string]$Note,
  [string]$NextStep,
  [string]$RootPath,
  [string]$TaskName = 'Rate Rater'
)

$ErrorActionPreference = 'Stop'

function Get-RateRaterRoot {
  if ($RootPath) { return $RootPath }
  if ($env:RATE_RATER_ROOT) { return $env:RATE_RATER_ROOT }
  return Join-Path $HOME '.codex/rate-rater'
}

function Ensure-RateRaterRoot {
  $root = Get-RateRaterRoot
  if (-not (Test-Path -LiteralPath $root)) {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
  }
  return $root
}

function Get-RateRaterPaths {
  $root = Ensure-RateRaterRoot
  [pscustomobject]@{
    Root   = $root
    Log    = Join-Path $root 'tasks.jsonl'
    Resume = Join-Path $root 'resume-queue.md'
  }
}

function New-RateRaterId {
  return ([guid]::NewGuid().ToString())
}

function Write-RateRaterEvent {
  param(
    [Parameter(Mandatory)]
    [string]$Event,
    [Parameter(Mandatory)]
    [string]$Id,
    [hashtable]$Data = @{}
  )

  $paths = Get-RateRaterPaths
  $record = [ordered]@{
    timestamp = (Get-Date).ToString('o')
    event     = $Event
    taskId    = $Id
  }
  foreach ($key in $Data.Keys) {
    $record[$key] = $Data[$key]
  }
  $line = ($record | ConvertTo-Json -Compress -Depth 6)
  Add-Content -LiteralPath $paths.Log -Value $line -Encoding utf8
  return $record
}

function Read-RateRaterLog {
  $paths = Get-RateRaterPaths
  if (-not (Test-Path -LiteralPath $paths.Log)) { return @() }

  Get-Content -LiteralPath $paths.Log | ForEach-Object {
    if ($_ -and $_.Trim()) {
      $_ | ConvertFrom-Json
    }
  }
}

function Get-RateRaterState {
  $state = @{}
  foreach ($entry in Read-RateRaterLog) {
    $id = $entry.taskId
    if (-not $id) { continue }
    if (-not $state.ContainsKey($id)) {
      $state[$id] = [ordered]@{
        taskId = $id
        title = $null
        scope = $null
        remaining5h = $null
        remainingWeekly = $null
        status = 'unknown'
        nextStep = $null
        lastNote = $null
        updatedAt = $null
      }
    }

    $item = $state[$id]
    switch ($entry.event) {
      'start' {
        $item.title = $entry.title
        $item.scope = $entry.scope
        $item.remaining5h = $entry.remaining5h
        $item.remainingWeekly = $entry.remainingWeekly
        $item.status = 'running'
      }
      'note' {
        $item.lastNote = $entry.note
      }
      'block' {
        $item.status = 'blocked-usage'
        $item.nextStep = $entry.nextStep
      }
      'finish' {
        $item.status = 'finished'
      }
    }
    $item.updatedAt = $entry.timestamp
  }

  return $state.Values
}

function Write-RateRaterResumeQueue {
  $paths = Get-RateRaterPaths
  $open = Get-RateRaterState | Where-Object { $_.status -eq 'blocked-usage' } | Sort-Object updatedAt -Descending

  $lines = New-Object System.Collections.Generic.List[string]
  $lines.Add("# Rate Rater resume queue")
  $lines.Add("")
  $lines.Add("Generated: $(Get-Date -Format o)")
  $lines.Add("")

  if (-not $open) {
    $lines.Add("No incomplete tasks are blocked by usage limits.")
  } else {
    foreach ($task in $open) {
      $label = if ($task.title) { $task.title } else { $task.taskId }
      $lines.Add("## $label")
      $lines.Add("")
      $lines.Add("- Task ID: $($task.taskId)")
      $lines.Add("- Scope: $($task.scope)")
      $lines.Add("- Remaining 5h: $($task.remaining5h)")
      $lines.Add("- Remaining weekly: $($task.remainingWeekly)")
      $lines.Add("- Last note: $($task.lastNote)")
      $lines.Add("- Next step: $($task.nextStep)")
      $lines.Add("- Resume from: $($paths.Log)")
      $lines.Add("")
    }
  }

  Set-Content -LiteralPath $paths.Resume -Value $lines -Encoding utf8
  return $paths.Resume
}

function Install-RateRaterTask {
  $scriptPath = $PSCommandPath
  $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Mode hourly"
  $trigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) -RepetitionInterval (New-TimeSpan -Hours 1) -RepetitionDuration (New-TimeSpan -Days 3650)

  $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
  if ($existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
  }

  Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Rate Rater hourly resume queue'
}

switch ($Mode) {
  'start' {
    $id = if ($TaskId) { $TaskId } else { New-RateRaterId }
    if (-not $Title) { throw 'start requires -Title' }
    if (-not $Scope) { throw 'start requires -Scope' }
    Write-RateRaterEvent -Event 'start' -Id $id -Data @{
      title = $Title
      scope = $Scope
      remaining5h = $Remaining5h
      remainingWeekly = $RemainingWeekly
    } | Out-Null
    $id
  }
  'note' {
    if (-not $TaskId) { throw 'note requires -TaskId' }
    if (-not $Note) { throw 'note requires -Note' }
    Write-RateRaterEvent -Event 'note' -Id $TaskId -Data @{ note = $Note } | Out-Null
  }
  'block' {
    if (-not $TaskId) { throw 'block requires -TaskId' }
    Write-RateRaterEvent -Event 'block' -Id $TaskId -Data @{
      nextStep = $NextStep
      note = $Note
    } | Out-Null
  }
  'finish' {
    if (-not $TaskId) { throw 'finish requires -TaskId' }
    Write-RateRaterEvent -Event 'finish' -Id $TaskId -Data @{ note = $Note } | Out-Null
  }
  'hourly' {
    Write-RateRaterResumeQueue
  }
  'install' {
    Install-RateRaterTask
  }
}
