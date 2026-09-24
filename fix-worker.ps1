# Registra o Worker no EHR: enum + base Engineer (botao de duto).
# Rode na raiz do projeto:  C:\Users\User\EHR-novo
#   powershell -ExecutionPolicy Bypass -File .\fix-worker.ps1
# Usa a linha do Whistleblower (que ja existe) como ancora. Se algo nao for achado, aborta SEM alterar nada.

$ErrorActionPreference = 'Stop'

$root       = (Get-Location).Path
$helperPath = Join-Path $root 'Modules\CustomRolesHelper.cs'
$rolesPath  = Join-Path $root 'CustomRoles.cs'

foreach ($p in @($helperPath, $rolesPath)) {
    if (-not (Test-Path $p)) { throw "Arquivo nao encontrado: $p (rode o script na raiz do projeto)" }
}

# Confere se o Worker.cs existe de verdade (e nao e o modelo Class1 do Visual Studio)
$workerFile = @(Get-ChildItem -Path $root -Recurse -File -Filter 'Worker.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' })
if ($workerFile.Count -eq 0) {
    Write-Host 'AVISO: Worker.cs nao foi encontrado no projeto. Salve o arquivo antes de compilar.'
}
elseif ((Get-Content $workerFile[0].FullName -Raw) -notmatch 'class\s+Worker\s*:\s*RoleBase') {
    Write-Host "AVISO: $($workerFile[0].FullName) nao contem 'class Worker : RoleBase'. Substitua pelo conteudo correto."
}

function Read-Utf8([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $bom = ($bytes.Length -ge 3) -and ($bytes[0] -eq 0xEF) -and ($bytes[1] -eq 0xBB) -and ($bytes[2] -eq 0xBF)
    return [pscustomobject]@{ Text = [System.IO.File]::ReadAllText($path); Bom = $bom }
}

function Write-Utf8([string]$path, [string]$text, [bool]$bom) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($bom)))
}

function Get-NewLine([string]$text) {
    if ($text.Contains("`r`n")) { return "`r`n" } else { return "`n" }
}

function Add-After([string]$text, [string]$pattern, [string]$newLine, [string]$nl) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Esperava 1 ocorrencia, achei $count para o padrao: $pattern" }
    $sb = { param($m) $m.Value + $nl + $m.Groups[1].Value + $newLine }.GetNewClosure()
    $eval = [System.Text.RegularExpressions.MatchEvaluator]$sb
    return $rx.Replace($text, $eval)
}

# Backup fora do projeto
$backup = Join-Path $env:TEMP ('EHR-fix-worker-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item $helperPath (Join-Path $backup 'CustomRolesHelper.cs')
Copy-Item $rolesPath  (Join-Path $backup 'CustomRoles.cs')
Write-Host "Backup salvo em: $backup"

# ---------- 1. CustomRoles.cs (enum) ----------
$r  = Read-Utf8 $rolesPath
$nl = Get-NewLine $r.Text
$rolesText = [regex]::Replace($r.Text, '(?m)^[ \t]*Worker,[^\r\n]*\r?\n', '')
$rolesText = Add-After $rolesText '^([ \t]*)Whisperer,(?=[ \t]*\r?$)' 'Worker,' $nl

# ---------- 2. CustomRolesHelper.cs (base Engineer) ----------
$h  = Read-Utf8 $helperPath
$nl = Get-NewLine $h.Text
$helperText = $h.Text

if ($helperText -match 'CustomRoles\.Worker\b') {
    Write-Host 'CustomRolesHelper.cs ja tem o Worker, pulando essa parte.'
}
else {
    $helperText = Add-After $helperText `
        '^([ \t]*)CustomRoles\.Whistleblower => CustomRoles\.Engineer,(?=[ \t]*\r?$)' `
        'CustomRoles.Worker => CustomRoles.Engineer,' $nl
}

# ---------- Grava (so chega aqui se nada deu erro) ----------
Write-Utf8 $rolesPath  $rolesText  $r.Bom
Write-Utf8 $helperPath $helperText $h.Bom

Write-Host ''
Write-Host '=== CustomRolesHelper.cs ==='
Select-String -Path $helperPath -Pattern 'CustomRoles\.Worker\b' -Context 2, 2 | ForEach-Object {
    $_.Context.PreContext
    '>> ' + $_.Line
    $_.Context.PostContext
}

Write-Host ''
Write-Host '=== CustomRoles.cs (enum) ==='
Select-String -Path $rolesPath -Pattern '^\s*Worker,' -Context 1, 1 | ForEach-Object {
    $_.Context.PreContext
    '>> ' + $_.Line
    $_.Context.PostContext
}

Write-Host ''
Write-Host 'Pronto. Agora rode: dotnet build'