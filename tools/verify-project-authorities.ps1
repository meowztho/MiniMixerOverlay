param([switch]$SelfTest)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
function Assert-Package([switch]$Mutate) {
 $idx = Get-Content -LiteralPath (Join-Path $root 'PROJECT_INDEX.yaml') -Raw | ConvertFrom-Json
 $ac = Get-Content -LiteralPath (Join-Path $root 'ACCEPTANCE_MATRIX.yaml') -Raw | ConvertFrom-Json
 if ($idx.schema_version -ne 1 -or $ac.schema_version -ne 1) { throw 'Schema version mismatch' }
 if (@($idx.authorities.PSObject.Properties).Count -lt 12) { throw 'Missing canonical authorities' }
 $paths = @('AGENTS.md','PROJECT_INDEX.yaml','ACCEPTANCE_MATRIX.yaml','Docs/README.md')
 foreach($property in $idx.authorities.PSObject.Properties) {
   if (-not $property.Value.role -or -not $property.Value.path) { throw ('Malformed authority: '+$property.Name) }
   $paths += [string]$property.Value.path
 }
 if ($Mutate) { $paths += 'Docs/DOES_NOT_EXIST.md' }
 foreach($path in ($paths | Select-Object -Unique)) {
    $full = Join-Path $root ($path.Replace('/','\'))
    if(-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Missing authority: '+$path) }
 }
 foreach($task in $idx.workstreams.PSObject.Properties) {
    foreach($path in $task.Value) { if($paths -notcontains $path) { throw ('Unregistered workstream authority: '+$path) } }
 }
 $ids = @($ac.criteria | ForEach-Object { $_.id })
 if($ids.Count -lt 10 -or @($ids | Select-Object -Unique).Count -ne $ids.Count) { throw 'Acceptance IDs missing or duplicate' }
 $validStatus = @('unverified','in_progress','verified','needs_recheck','blocked')
 foreach($criterion in $ac.criteria) {
   if($validStatus -notcontains $criterion.status) { throw ('Bad status: '+$criterion.id) }
   if(-not $criterion.title -or -not $criterion.stage -or -not $criterion.evidence_required) { throw ('Incomplete criterion: '+$criterion.id) }
   foreach($dep in $criterion.depends_on) { if($ids -notcontains $dep) { throw ('Missing dependency: '+$dep) } }
   foreach($path in $criterion.sources) { if($paths -notcontains $path) { throw ('Unregistered criterion source: '+$path) } }
 }
 foreach($file in @('Docs/03_TECH_ARCHITECTURE.md','Docs/07_ROADMAP.md','Docs/10_DECISIONS_AND_EVIDENCE.md')) {
   if(-not (Test-Path -LiteralPath (Join-Path $root $file))) { throw ('Plan missing: '+$file) }
 }
 return $true
}
[void](Assert-Package)
if($SelfTest) {
 $caught = $false
 try { [void](Assert-Package -Mutate) } catch { $caught = $true }
 if(-not $caught) { throw 'Mutation test unexpectedly passed' }
 Write-Output 'PASS: authority/acceptance structure, links and missing-file mutant rejected'
} else {
 Write-Output 'PASS: authority and acceptance structure'
}
