# Tags and pushes a new FNF Tracker release. GitHub Actions then builds FNFTracker.exe and
# publishes it, and everyone's app offers the update on its next launch.
# Run release.cmd (double-click, or from a terminal).

# Git writes normal progress to stderr; failures are caught through $LASTEXITCODE instead.
$ErrorActionPreference = 'Continue'
Set-Location -Path $PSScriptRoot

function Fail($message) {
    Write-Host ""
    Write-Host $message -ForegroundColor Red
    exit 1
}

function Invoke-Git {
    # Runs git and returns its output as text; stops the script if git fails.
    $output = & git.exe @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { Fail ("git " + ($args -join ' ') + " failed:`n" + ($output -join "`n")) }
    return $output
}

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Fail "Git isn't installed or isn't on PATH." }

Write-Host "FNF Tracker release" -ForegroundColor Yellow
Write-Host ""

# 1. Must be on main with nothing uncommitted.
$branch = (Invoke-Git rev-parse --abbrev-ref HEAD | Select-Object -First 1).Trim()
if ($branch -ne 'main') { Fail "You're on branch '$branch'. Switch to main first:  git checkout main" }

$dirty = Invoke-Git status --porcelain
if ($dirty) {
    Write-Host "You have changes that aren't committed:" -ForegroundColor Red
    $dirty | ForEach-Object { Write-Host "  $_" }
    Fail "Commit and push them (or undo them) before releasing."
}

# 2. Must match GitHub exactly, so the release is built from what you tested.
Write-Host "Checking GitHub for the latest changes..."
Invoke-Git fetch origin --tags --quiet | Out-Null
$local  = (Invoke-Git rev-parse HEAD | Select-Object -First 1).Trim()
$remote = (Invoke-Git rev-parse origin/main | Select-Object -First 1).Trim()
if ($local -ne $remote) {
    $behind = [int]((Invoke-Git rev-list --count HEAD..origin/main | Select-Object -First 1).Trim())
    $ahead  = [int]((Invoke-Git rev-list --count origin/main..HEAD | Select-Object -First 1).Trim())
    if ($ahead -gt 0) { Fail "You have $ahead commit(s) that aren't on GitHub yet. Run:  git push" }
    if ($behind -gt 0) {
        $answer = Read-Host "GitHub has $behind newer commit(s). Pull them now? [Y/n]"
        if ($answer -match '^[Nn]') { Fail "Release cancelled. Pull, test, then run this again." }
        Invoke-Git pull --ff-only --quiet | Out-Null
        Write-Host ""
        Write-Host "Pulled. You haven't tried those changes yet, so build and test first, then run this again." -ForegroundColor Yellow
        exit 1
    }
}

# 3. Work out the next version.
$tags = Invoke-Git tag --list "v*" --sort=-v:refname
$last = $tags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } | Select-Object -First 1
if ($last) {
    $lastVersion = [version]($last.Substring(1))
    $suggest = "v{0}.{1}.{2}" -f $lastVersion.Major, $lastVersion.Minor, ($lastVersion.Build + 1)
    $feature = "v{0}.{1}.0" -f $lastVersion.Major, ($lastVersion.Minor + 1)
    Write-Host "Last release: $last"
    Write-Host ""
    Write-Host "Changes since ${last}:"
    $log = Invoke-Git log --oneline "$last..HEAD"
    if ($log) { $log | ForEach-Object { Write-Host "  $_" } }
    else {
        Write-Host "  (none)"
        $answer = Read-Host "Nothing has changed since $last. Release anyway? [y/N]"
        if ($answer -notmatch '^[Yy]') { Fail "Release cancelled." }
    }
    Write-Host ""
    Write-Host "Use $suggest for fixes and small tweaks, $feature for new features."
} else {
    $lastVersion = [version]"0.0.0"
    $suggest = "v0.1.0"
}

$entered = Read-Host "New version [$suggest]"
if ([string]::IsNullOrWhiteSpace($entered)) { $entered = $suggest }
$entered = $entered.Trim()
if (-not $entered.StartsWith('v')) { $entered = "v$entered" }
if ($entered -notmatch '^v\d+\.\d+\.\d+$') { Fail "'$entered' isn't a version like v1.2.3." }

$newVersion = [version]($entered.Substring(1))
if ($newVersion -le $lastVersion) { Fail "$entered isn't higher than $last. The app only offers newer versions." }
if ($tags -contains $entered) { Fail "$entered already exists. Pick a higher number." }

# 4. Confirm, tag, push.
Write-Host ""
$answer = Read-Host "Release $entered to everyone? [y/N]"
if ($answer -notmatch '^[Yy]') { Fail "Release cancelled. Nothing was changed." }

Invoke-Git tag -a $entered -m "FNF Tracker $($entered.Substring(1))" | Out-Null
$push = & git.exe push origin $entered 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) {
    & git.exe tag -d $entered 2>&1 | Out-Null
    Fail ("Couldn't push the tag, so nothing was released:`n" + ($push -join "`n"))
}

$repo = "https://github.com/Hooee71/FNFTracker"
Write-Host ""
Write-Host "$entered is on its way." -ForegroundColor Green
Write-Host "GitHub is building it now (a few minutes):  $repo/actions"
Write-Host "It will appear here when done:              $repo/releases/latest"
Write-Host ""
Write-Host "If the build goes red, nothing is published. To retry the same number after a fix:"
Write-Host "  git tag -d $entered"
Write-Host "  git push origin :refs/tags/$entered"
Write-Host ""
$answer = Read-Host "Open the build page in your browser? [Y/n]"
if ($answer -notmatch '^[Nn]') { Start-Process "$repo/actions" }
