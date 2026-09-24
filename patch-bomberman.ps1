# Registra o BomberMan como role de IMPOSTOR (base Phantom) no EHR.
# Rode na raiz do projeto:  C:\Users\User\EHR-novo
#   powershell -ExecutionPolicy Bypass -File .\patch-bomberman.ps1
#
# O que faz:
#   1. CustomRoles.cs: poe "BomberMan," no bloco de Impostors (antes de Chronomancer).
#   2. Modules\CustomRolesHelper.cs: copia o BomberMan para os 3 lugares onde o Escapist define
#      base vanilla, time Impostor e categoria (aqui a base e Phantom).
# Usa o Escapist e o Chronomancer como ancoras. Se algum trecho nao for encontrado,
# aborta SEM alterar nada.

$ErrorActionPreference = 'Stop'

$root       = (Get-Location).Path
$helperPath = Join-Path $root 'Modules\CustomRolesHelper.cs'
$rolesPath  = Join-Path $root 'CustomRoles.cs'

foreach ($p in @($helperPath, $rolesPath)) {
    if (-not (Test-Path $p)) { throw "Arquivo nao encontrado: $p (rode o script na raiz do projeto)" }
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

# Insere $newLine logo abaixo da unica linha que casa com $pattern (mantendo a indentacao).
function Add-After([string]$text, [string]$pattern, [string]$newLine, [string]$nl) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Esperava 1 ocorrencia, achei $count para o padrao: $pattern" }
    $sb = { param($m) $m.Value + $nl + $m.Groups[1].Value + $newLine }.GetNewClosure()
    $eval = [System.Text.RegularExpressions.MatchEvaluator]$sb
    return $rx.Replace($text, $eval)
}

# Insere $newLine logo ACIMA da unica linha que casa com $pattern (mantendo a indentacao).
function Add-Before([string]$text, [string]$pattern, [string]$newLine, [string]$nl) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Esperava 1 ocorrencia, achei $count para o padrao: $pattern" }
    $sb = { param($m) $m.Groups[1].Value + $newLine + $nl + $m.Value }.GetNewClosure()
    $eval = [System.Text.RegularExpressions.MatchEvaluator]$sb
    return $rx.Replace($text, $eval)
}

# Backup fora do projeto
$backup = Join-Path $env:TEMP ('EHR-bomberman-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
Copy-Item $helperPath (Join-Path $backup 'CustomRolesHelper.cs')
Copy-Item $rolesPath  (Join-Path $backup 'CustomRoles.cs')
Write-Host "Backup salvo em: $backup"

# ---------- 1. CustomRoles.cs (enum) ----------
$r  = Read-Utf8 $rolesPath
$nl = Get-NewLine $r.Text

if ($r.Text -notmatch '(?m)^[ \t]*Phantom,') {
    throw 'O enum CustomRoles nao tem "Phantom," (role vanilla usada como base). Me mande o resultado de: git grep -n "Phantom" -- CustomRoles.cs'
}

$rolesText = [regex]::Replace($r.Text, '(?m)^[ \t]*BomberMan,[^\r\n]*\r?\n', '')   # remove qualquer "BomberMan," ja existente (mesmo com comentario)
$rolesText = Add-Before $rolesText '^([ \t]*)Chronomancer,(?=[ \t]*\r?$)' 'BomberMan,' $nl

# ---------- 2. CustomRolesHelper.cs ----------
$h  = Read-Utf8 $helperPath
$nl = Get-NewLine $h.Text
$helperText = $h.Text

if ($helperText -match 'CustomRoles\.BomberMan') {
    Write-Host 'CustomRolesHelper.cs ja tem o BomberMan, pulando essa parte.'
}
else {
    # Base vanilla: Phantom
    $helperText = Add-After $helperText `
        '^([ \t]*)CustomRoles\.Escapist => UsePets \? CustomRoles\.Impostor : CustomRoles\.Shapeshifter,(?=[ \t]*\r?$)' `
        'CustomRoles.BomberMan => CustomRoles.Phantom,' $nl

    # Lista de impostores (a linha "CustomRoles.Escapist or")
    $helperText = Add-After $helperText `
        '^([ \t]*)CustomRoles\.Escapist or(?=[ \t]*\r?$)' `
        'CustomRoles.BomberMan or' $nl

    # Categoria da role nas opcoes
    $helperText = Add-After $helperText `
        '^([ \t]*)CustomRoles\.Escapist => RoleOptionType\.Impostor_Concealing,(?=[ \t]*\r?$)' `
        'CustomRoles.BomberMan => RoleOptionType.Impostor_Concealing,' $nl
}

# ---------- Grava (so chega aqui se nada deu erro) ----------
Write-Utf8 $rolesPath  $rolesText  $r.Bom
Write-Utf8 $helperPath $helperText $h.Bom

Write-Host ''
Write-Host '=== Trechos alterados em CustomRolesHelper.cs ==='
Select-String -Path $helperPath -Pattern 'CustomRoles\.BomberMan' -Context 3, 3 | ForEach-Object {
    $_.Context.PreContext
    '>> ' + $_.Line
    $_.Context.PostContext
    '-----'
}

Write-Host ''
Write-Host '=== Linha no enum (CustomRoles.cs) ==='
Select-String -Path $rolesPath -Pattern 'BomberMan,' -Context 1, 1 | ForEach-Object {
    $_.Context.PreContext
    '>> ' + $_.Line
    $_.Context.PostContext
}

Write-Host ''
Write-Host 'Pronto. Agora rode: dotnet build'