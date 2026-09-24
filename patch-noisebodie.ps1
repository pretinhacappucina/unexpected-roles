# Registra o Noisebodie como role NEUTRA no EHR.
# Rode na raiz do projeto:  C:\Users\User\EHR-novo
#   powershell -ExecutionPolicy Bypass -File .\patch-noisebodie.ps1
#
# O que faz (usando o Jester como modelo, mas SEM copiar o comportamento dele):
#   1. CustomRoles.cs: poe "Noisebodie," no bloco de Neutrals (antes de Quarry).
#   2. Modules\CustomRolesHelper.cs: base Crewmate e categoria Neutral_Evil (linhas do Jester).
#   3. enum CustomWinner: cria a vitoria Noisebodie, no mesmo formato do Jester (achado automaticamente).
#   4. Cor da role: se achar a linha de cor do Jester, cria uma para o Noisebodie.
# Se algo essencial nao for achado, aborta SEM alterar nada.

$ErrorActionPreference = 'Stop'

$root       = (Get-Location).Path
$rolesPath  = Join-Path $root 'CustomRoles.cs'
$helperPath = Join-Path $root 'Modules\CustomRolesHelper.cs'

foreach ($p in @($rolesPath, $helperPath)) {
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

function Add-After([string]$text, [string]$pattern, [string]$newLine, [string]$nl) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Esperava 1 ocorrencia, achei $count para o padrao: $pattern" }
    $sb = { param($m) $m.Value + $nl + $m.Groups[1].Value + $newLine }.GetNewClosure()
    $eval = [System.Text.RegularExpressions.MatchEvaluator]$sb
    return $rx.Replace($text, $eval)
}

function Add-Before([string]$text, [string]$pattern, [string]$newLine, [string]$nl) {
    $rx = [regex]::new($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $count = $rx.Matches($text).Count
    if ($count -ne 1) { throw "Esperava 1 ocorrencia, achei $count para o padrao: $pattern" }
    $sb = { param($m) $m.Groups[1].Value + $newLine + $nl + $m.Value }.GetNewClosure()
    $eval = [System.Text.RegularExpressions.MatchEvaluator]$sb
    return $rx.Replace($text, $eval)
}

# Cache de documentos: nada e gravado ate o fim
$docs = @{}
function Get-Doc([string]$path) {
    if (-not $docs.ContainsKey($path)) {
        $d = Read-Utf8 $path
        $docs[$path] = [pscustomobject]@{ Path = $path; Text = $d.Text; Bom = $d.Bom; Orig = $d.Text }
    }
    return $docs[$path]
}

$csFiles = @(Get-ChildItem -Path $root -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' })

# Confere se o Noisebodie.cs existe de verdade
$roleFile = @($csFiles | Where-Object { $_.Name -eq 'Noisebodie.cs' })
if ($roleFile.Count -eq 0) {
    Write-Host 'AVISO: Noisebodie.cs nao foi encontrado no projeto. Salve o arquivo antes de compilar.'
}
elseif ((Get-Content $roleFile[0].FullName -Raw) -notmatch 'class\s+Noisebodie\s*:\s*RoleBase') {
    Write-Host "AVISO: $($roleFile[0].FullName) nao contem 'class Noisebodie : RoleBase'. Substitua pelo conteudo correto."
}

# ---------- 1. CustomRoles.cs ----------
$roles = Get-Doc $rolesPath
$nl = Get-NewLine $roles.Text
$roles.Text = [regex]::Replace($roles.Text, '(?m)^[ \t]*Noisebodie,[^\r\n]*\r?\n', '')
$roles.Text = Add-Before $roles.Text '^([ \t]*)Quarry,(?=[ \t]*(//[^\r\n]*)?\r?$)' 'Noisebodie,' $nl

# ---------- 2. CustomRolesHelper.cs ----------
$helper = Get-Doc $helperPath
$nl = Get-NewLine $helper.Text
if ($helper.Text -match 'CustomRoles\.Noisebodie\b') {
    Write-Host 'CustomRolesHelper.cs ja tem o Noisebodie, pulando essa parte.'
}
else {
    $helper.Text = Add-After $helper.Text `
        '^([ \t]*)CustomRoles\.Jester => Jester\.JesterCanVent\.GetBool\(\) \? CustomRoles\.Engineer : CustomRoles\.Crewmate,(?=[ \t]*\r?$)' `
        'CustomRoles.Noisebodie => CustomRoles.Crewmate,' $nl

    $helper.Text = Add-After $helper.Text `
        '^([ \t]*)CustomRoles\.Jester => RoleOptionType\.Neutral_Evil,(?=[ \t]*\r?$)' `
        'CustomRoles.Noisebodie => RoleOptionType.Neutral_Evil,' $nl
}

# ---------- 3. enum CustomWinner ----------
$winnerHits = @($csFiles | Select-String -Pattern 'enum\s+CustomWinner\b' -List)
if ($winnerHits.Count -ne 1) {
    throw "Esperava 1 arquivo com 'enum CustomWinner', achei $($winnerHits.Count). Me mande: git grep -n 'enum CustomWinner'"
}
$winnerPath = $winnerHits[0].Path
if ($winnerPath -eq $rolesPath) { throw 'O enum CustomWinner esta no mesmo arquivo do CustomRoles.cs. Me avise para eu ajustar o script.' }

$winner = Get-Doc $winnerPath
$nl = Get-NewLine $winner.Text
$winner.Text = [regex]::Replace($winner.Text, '(?m)^[ \t]*Noisebodie\b[^\r\n]*\r?\n', '')

$fmt = [regex]::new('(?m)^[ \t]*Jester(\s*=\s*CustomRoles\.Jester)?,')
$fm = $fmt.Matches($winner.Text)
if ($fm.Count -ne 1) {
    throw "Nao achei a linha do Jester no enum CustomWinner ($winnerPath) no formato esperado. Me mande o conteudo desse enum."
}
if ($fm[0].Groups[1].Success -and $fm[0].Groups[1].Value.Length -gt 0) { $newMember = 'Noisebodie = CustomRoles.Noisebodie,' }
else { $newMember = 'Noisebodie,' }

$winner.Text = Add-After $winner.Text '^([ \t]*)Jester(?:\s*=\s*CustomRoles\.Jester)?,[^\r\n]*(?=\r?$)' $newMember $nl

# ---------- 4. Cor da role ----------
$colorPattern = '^([ \t]*)\{[ \t]*CustomRoles\.Jester[ \t]*,[ \t]*"#[0-9A-Fa-f]{6,8}"[ \t]*\},[ \t]*(?=\r?$)'
$colorFiles = @()
foreach ($f in $csFiles) {
    $doc = Get-Doc $f.FullName
    if ([regex]::IsMatch($doc.Text, $colorPattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)) { $colorFiles += $doc }
}
if ($colorFiles.Count -eq 1 -and $colorFiles[0].Text -notmatch '\{[ \t]*CustomRoles\.Noisebodie[ \t]*,') {
    $c = $colorFiles[0]
    $nlc = Get-NewLine $c.Text
    $c.Text = Add-After $c.Text $colorPattern '{ CustomRoles.Noisebodie, "#ffd54f" },' $nlc
    Write-Host "Cor adicionada em: $($c.Path)"
}
elseif ($colorFiles.Count -eq 0) {
    Write-Host 'AVISO: nao achei a linha de cor do Jester. Se o Noisebodie ficar sem cor, me mande: git grep -n "CustomRoles.Jester" -- Main.cs'
}
elseif ($colorFiles.Count -gt 1) {
    Write-Host 'AVISO: achei a linha de cor do Jester em mais de um arquivo; nao alterei nenhum. Me avise.'
}

# ---------- Grava so o que mudou ----------
$backup = Join-Path $env:TEMP ('EHR-noisebodie-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null

$changed = @($docs.Values | Where-Object { $_.Text -ne $_.Orig })
foreach ($d in $changed) {
    $leaf = Split-Path $d.Path -Leaf
    Copy-Item $d.Path (Join-Path $backup ((Split-Path (Split-Path $d.Path -Parent) -Leaf) + '__' + $leaf))
    Write-Utf8 $d.Path $d.Text $d.Bom
}
Write-Host "Backup salvo em: $backup"
Write-Host ''
Write-Host 'Arquivos alterados:'
$changed | ForEach-Object { '  ' + $_.Path }

Write-Host ''
Write-Host '=== Trechos com Noisebodie ==='
foreach ($d in $changed) {
    Select-String -Path $d.Path -Pattern 'Noisebodie' -Context 1, 1 | ForEach-Object {
        '[' + (Split-Path $d.Path -Leaf) + ']'
        $_.Context.PreContext
        '>> ' + $_.Line
        $_.Context.PostContext
        '-----'
    }
}

Write-Host ''
Write-Host 'Pronto. Agora rode: dotnet build'