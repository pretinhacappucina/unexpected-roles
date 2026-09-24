# corrigir-ehr.ps1
#
# O que este script faz (nesta ordem):
#   1) Guarda suas alteracoes locais (git stash, nada e apagado) e troca o codigo para a tag
#      $Ref (padrao: v8.0.1), a versao publicada pelo mantenedor com suporte ao Among Us v18.0.
#   2) Usa o EHR.csproj do PROPRIO repositorio (nao reescreve mais o csproj).
#   3) Roda "dotnet build -c Release".
#   4) Se faltarem Hazel / Discord / TextCore / Newtonsoft, adiciona essas referencias
#      apenas durante o build (Directory.Build.props temporario) e tenta de novo.
#   5) Se ainda houver erro, mostra as linhas de codigo em volta de cada erro e salva tudo
#      em diagnostico-ehr.txt (e so colar esse arquivo aqui).
#   6) Se compilar, confirma o EHR.dll em BepInEx\plugins.
#
# Uso (dentro da pasta EHR-novo, com o Among Us FECHADO):
#   powershell -ExecutionPolicy Bypass -File .\corrigir-ehr.ps1
#
# Opcoes:
#   -Ref main            usa outra tag/branch em vez da v8.0.1
#   -SkipCheckout        nao troca de versao (compila o que esta na pasta)
#   -AmongUs "D:\...\Among Us"   caminho do jogo (so usado se nao houver local.props)

param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$AmongUs = 'D:\SteamLibrary\steamapps\common\Among Us',
    [string]$Ref = 'v8.0.1',
    [switch]$SkipCheckout
)

# 'Continue' de proposito: git escreve mensagens normais em stderr e 'Stop' derrubaria o script.
$ErrorActionPreference = 'Continue'
Set-Location -LiteralPath $ProjectPath

function Step([string]$t) { Write-Host ''; Write-Host "=== $t ===" -ForegroundColor Cyan }
function Ok([string]$t)   { Write-Host $t -ForegroundColor Green }
function Warn([string]$t) { Write-Host $t -ForegroundColor Yellow }
function Fail([string]$t) { Write-Host $t -ForegroundColor Red }

$report = New-Object System.Collections.Generic.List[string]
function Rep([string]$t) { [void]$report.Add($t) }

foreach ($tool in 'git', 'dotnet') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "ERRO: '$tool' nao encontrado no PATH."
        exit 1
    }
}
if (-not (Test-Path 'EHR.csproj')) {
    Fail "ERRO: EHR.csproj nao encontrado em $ProjectPath"
    exit 1
}

Rep ('Data: ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'))
Rep ('dotnet SDK: ' + (dotnet --version))

# ---------------------------------------------------------------
Step "1) Codigo-fonte: $Ref"

function Test-Tag([string]$name) {
    $null = git rev-parse -q --verify ('refs/tags/' + $name + '^{commit}') 2>$null
    return ($LASTEXITCODE -eq 0)
}

$headBefore = (git log -1 --format='%h %s' 2>$null)
Write-Host "Commit atual: $headBefore"
Rep "Commit inicial: $headBefore"

if ($SkipCheckout) {
    Warn 'Pulando a troca de versao (-SkipCheckout).'
}
else {
    $dirty = git status --porcelain --untracked-files=no
    if ($dirty) {
        Warn 'Ha alteracoes locais em arquivos versionados. Guardando com git stash (nada e apagado):'
        $dirty | ForEach-Object { Write-Host "   $_" }
        git stash push -m "backup-antes-de-$Ref" 2>&1 | Out-Host
        Rep "Alteracoes locais guardadas no stash 'backup-antes-de-$Ref'."
    }

    if ((git rev-parse --is-shallow-repository 2>$null) -eq 'true') {
        Warn 'Clone raso detectado; buscando o historico completo...'
        git fetch --unshallow --tags --force 2>&1 | Out-Host
    }

    git fetch origin --tags --force 2>&1 | Out-Host

    if (-not (Test-Tag $Ref)) {
        Warn "Tag '$Ref' nao encontrada em 'origin'. Tentando o repositorio oficial (upstream)..."
        $remotes = @(git remote)
        if ($remotes -notcontains 'upstream') {
            git remote add upstream https://github.com/Gurge44/EndlessHostRoles.git 2>&1 | Out-Host
        }
        git fetch upstream --tags --force 2>&1 | Out-Host
    }

    git checkout $Ref 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { git checkout "origin/$Ref" 2>&1 | Out-Host }
    if ($LASTEXITCODE -ne 0) {
        Fail "ERRO: nao consegui trocar para '$Ref'. Confira sua internet e rode: git fetch --all --tags"
        exit 1
    }
}

$headNow = (git log -1 --format='%h %s' 2>$null)
$descNow = (git describe --tags --always 2>$null)
Ok "Codigo agora em: $headNow  ($descNow)"
Rep "Commit usado no build: $headNow ($descNow)"

# ---------------------------------------------------------------
Step '2) Configuracao local'

if (Get-Process -Name 'Among Us' -ErrorAction SilentlyContinue) {
    Warn 'O Among Us esta aberto. Feche-o, senao a copia do EHR.dll para BepInEx\plugins vai falhar.'
}

if (-not (Test-Path 'local.props')) {
    $esc = [System.Security.SecurityElement]::Escape($AmongUs)
    $xml = "<Project>`r`n  <PropertyGroup>`r`n    <AmongUsPath>$esc</AmongUsPath>`r`n  </PropertyGroup>`r`n</Project>`r`n"
    [System.IO.File]::WriteAllText((Join-Path $ProjectPath 'local.props'), $xml, (New-Object System.Text.UTF8Encoding($false)))
    Ok "local.props criado (AmongUsPath = $AmongUs)"
}
else {
    $lp = Get-Content -LiteralPath 'local.props' -Raw
    $mm = [regex]::Match($lp, '<AmongUsPath>\s*(.*?)\s*</AmongUsPath>')
    if ($mm.Success) {
        $AmongUs = [System.Net.WebUtility]::HtmlDecode($mm.Groups[1].Value)
        Ok "local.props ja existe (mantido). AmongUsPath = $AmongUs"
    }
    else {
        Warn 'local.props existe, mas sem <AmongUsPath>. Usando o caminho do parametro -AmongUs.'
    }
}
Rep "AmongUsPath: $AmongUs"

if (-not (Test-Path -LiteralPath $AmongUs)) {
    Warn "Pasta do jogo nao encontrada: $AmongUs (a compilacao funciona, mas a copia automatica para BepInEx\plugins nao)."
}

Write-Host 'Referencias do jogo no EHR.csproj:'
Select-String -Path 'EHR.csproj' -Pattern 'GameLibs' | ForEach-Object {
    $line = '   ' + $_.Line.Trim()
    Write-Host $line
    Rep $line
}

# ---------------------------------------------------------------
$log = Join-Path $ProjectPath 'build.log'

function Invoke-Build([bool]$clean) {
    if ($clean) { Remove-Item -Recurse -Force 'bin', 'obj' -ErrorAction SilentlyContinue }
    dotnet build -c Release -nologo -tl:off 2>&1 | Tee-Object -FilePath $log | Out-Host
    return $LASTEXITCODE
}

function Get-BuildErrors {
    $rx = '^\s*(?<file>[A-Za-z]:\\.+?)\((?<line>\d+),(?<col>\d+)\): error (?<code>[A-Z]+\d+): (?<msg>.*?)(?:\s+\[[^\]]+\])?\s*$'
    $seen = @{}
    $out = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $log)) { return @() }
    foreach ($l in (Get-Content -LiteralPath $log)) {
        $m = [regex]::Match($l, $rx)
        if (-not $m.Success) { continue }
        $key = $m.Groups['file'].Value + '|' + $m.Groups['line'].Value + '|' + $m.Groups['col'].Value + '|' + $m.Groups['code'].Value
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        [void]$out.Add([pscustomobject]@{
            File = $m.Groups['file'].Value
            Line = [int]$m.Groups['line'].Value
            Col  = [int]$m.Groups['col'].Value
            Code = $m.Groups['code'].Value
            Msg  = $m.Groups['msg'].Value
        })
    }
    return $out.ToArray()
}

function Get-OtherErrors {
    $res = @{}
    if (-not (Test-Path -LiteralPath $log)) { return @() }
    foreach ($l in (Get-Content -LiteralPath $log)) {
        if ($l -match '\berror (NU|MSB|NETSDK)\d+' -and $l -notmatch '\(\d+,\d+\): error') {
            $res[$l.Trim()] = $true
        }
    }
    return @($res.Keys)
}

function Show-Context($e) {
    if (-not (Test-Path -LiteralPath $e.File)) { return }
    $lines = @(Get-Content -LiteralPath $e.File)
    $a = [Math]::Max(1, $e.Line - 4)
    $b = [Math]::Min($lines.Count, $e.Line + 4)
    for ($i = $a; $i -le $b; $i++) {
        $mark = if ($i -eq $e.Line) { '>>' } else { '  ' }
        $txt = '{0} {1,5}: {2}' -f $mark, $i, $lines[$i - 1]
        if ($i -eq $e.Line) { Write-Host $txt -ForegroundColor White } else { Write-Host $txt -ForegroundColor DarkGray }
        Rep $txt
    }
}

function Find-Dll([string]$name, [bool]$allowInterop) {
    $dirs = @((Join-Path $ProjectPath 'GameLibs'))
    if ($allowInterop) { $dirs += (Join-Path $AmongUs 'BepInEx\interop') }
    $dirs += (Join-Path $AmongUs 'Among Us_Data\Managed')
    $dirs += (Join-Path $AmongUs 'BepInEx\core')
    foreach ($d in $dirs) {
        $p = Join-Path $d $name
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $nuget = Join-Path $env:USERPROFILE '.nuget\packages'
    foreach ($pkg in 'classicus.gamelibs', 'amongus.gamelibs.steam') {
        $root = Join-Path $nuget $pkg
        if (Test-Path -LiteralPath $root) {
            $hit = Get-ChildItem -Path $root -Recurse -Filter $name -File -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
            if ($hit) { return $hit.FullName }
        }
    }
    return $null
}

# ---------------------------------------------------------------
Step '3) dotnet build -c Release'
$code = Invoke-Build $true
$errs = @(Get-BuildErrors)
$tempProps = $false
$propsPath = Join-Path $ProjectPath 'Directory.Build.props'

# Fallback: assemblies do jogo que nao vieram no pacote de referencias
if ($code -ne 0) {
    $missing = @($errs | Where-Object { $_.Code -in 'CS0246', 'CS0234', 'CS0012' })
    if ($missing.Count -gt 0) {
        if (Test-Path -LiteralPath $propsPath) {
            Warn 'Faltam referencias, mas ja existe um Directory.Build.props no projeto; nao vou mexer nele.'
        }
        else {
            Step '3b) Faltam referencias: tentando adiciona-las so para este build'
            $wanted = @(
                @('Hazel', 'Hazel.dll', 'Hazel', $true),
                @('Discord', 'Discord.dll', 'Discord', $true),
                @('UnityEngine.TextCoreModule', 'UnityEngine.TextCoreModule.dll', 'TextCore', $true),
                @('Newtonsoft.Json', 'Newtonsoft.Json.dll', 'Newtonsoft', $false)
            )
            $items = New-Object System.Collections.Generic.List[string]
            foreach ($w in $wanted) {
                $mentioned = @($missing | Where-Object { $_.Msg -match $w[2] })
                if ($mentioned.Count -eq 0) { continue }
                $p = Find-Dll $w[1] $w[3]
                if ($p) {
                    $hp = [System.Security.SecurityElement]::Escape($p)
                    [void]$items.Add(('    <Reference Include="{0}"><HintPath>{1}</HintPath><Private>false</Private></Reference>' -f $w[0], $hp))
                    Ok ("  {0,-30} {1}" -f $w[0], $p)
                }
                elseif ($w[0] -eq 'Newtonsoft.Json') {
                    [void]$items.Add('    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />')
                    Warn '  Newtonsoft.Json                usando o pacote NuGet 13.0.3'
                }
                else {
                    Warn ("  {0,-30} NAO ENCONTRADO" -f $w[0])
                }
            }
            if ($items.Count -gt 0) {
                $props = "<Project>`r`n  <ItemGroup>`r`n" + ($items -join "`r`n") + "`r`n  </ItemGroup>`r`n</Project>`r`n"
                [System.IO.File]::WriteAllText($propsPath, $props, (New-Object System.Text.UTF8Encoding($false)))
                $tempProps = $true
                Rep 'Directory.Build.props temporario criado com referencias extras:'
                $items | ForEach-Object { Rep $_ }
                $code = Invoke-Build $false
                $errs = @(Get-BuildErrors)
            }
            else {
                Warn 'Nenhuma das DLLs conhecidas foi encontrada; nada a adicionar.'
            }
        }
    }
}

if ($tempProps) { Remove-Item -LiteralPath $propsPath -Force -ErrorAction SilentlyContinue }

# ---------------------------------------------------------------
if ($code -ne 0) {
    Step '4) Diagnostico dos erros'
    Fail "O build falhou (codigo $code). Erros unicos: $($errs.Count)"
    Rep "Build falhou (codigo $code). Erros unicos: $($errs.Count)"

    $shown = 0
    foreach ($e in $errs) {
        if ($shown -ge 15) { Warn '(mostrando so os 15 primeiros)'; break }
        $shown++
        $rel = $e.File.Replace($ProjectPath + '\', '')
        Write-Host ''
        Write-Host ("[{0}] {1}({2},{3})" -f $e.Code, $rel, $e.Line, $e.Col) -ForegroundColor Red
        Write-Host ("     " + $e.Msg) -ForegroundColor Red
        Rep ''
        Rep ("[{0}] {1}({2},{3})" -f $e.Code, $rel, $e.Line, $e.Col)
        Rep ("     " + $e.Msg)
        Show-Context $e
    }

    $others = @(Get-OtherErrors)
    foreach ($o in $others) { Write-Host $o -ForegroundColor Red; Rep $o }

    if ($errs.Count -eq 0 -and $others.Count -eq 0 -and (Test-Path -LiteralPath $log)) {
        Warn 'Nao consegui identificar os erros automaticamente; incluindo o final do build.log no diagnostico.'
        Rep ''
        Rep '--- final do build.log ---'
        Get-Content -LiteralPath $log -Tail 40 | ForEach-Object { Rep $_ }
    }

    Write-Host ''
    if (@($errs | Where-Object { $_.Code -eq 'CS1503' -and $_.Msg -match 'PlayerId' }).Count -gt 0) {
        Warn 'Dica: o tipo de PlayerId mudou na API do jogo. Se o erro persistir na v8.0.1, cole o diagnostico aqui para eu ajustar a linha exata.'
    }
    if ($log -and (Select-String -Path $log -Pattern 'MSB3027|MSB3021' -Quiet)) {
        Warn 'Dica: a copia do EHR.dll falhou. Feche o Among Us (e o que estiver usando o arquivo) e rode de novo.'
    }
    if ($log -and (Select-String -Path $log -Pattern 'NU1101|NU1102|NU1301' -Quiet)) {
        Warn 'Dica: falha ao baixar pacotes NuGet. Confira a internet e rode de novo.'
    }

    $reportPath = Join-Path $ProjectPath 'diagnostico-ehr.txt'
    [System.IO.File]::WriteAllLines($reportPath, $report, (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ''
    Write-Host "Diagnostico salvo em: $reportPath" -ForegroundColor Cyan
    Write-Host 'Cole o conteudo desse arquivo aqui na conversa.' -ForegroundColor Cyan
    exit $code
}

# ---------------------------------------------------------------
Step '5) Resultado'
$dll = Join-Path $ProjectPath 'bin\Release\net6.0\EHR.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    $hit = Get-ChildItem -Path (Join-Path $ProjectPath 'bin') -Recurse -Filter 'EHR.dll' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($hit) { $dll = $hit.FullName }
}

if (-not (Test-Path -LiteralPath $dll)) {
    Fail 'O build terminou sem erro, mas nao achei o EHR.dll. Rode: dir /s /b EHR.dll'
    exit 1
}
Ok "DLL compilada: $dll"

$plugins = Join-Path $AmongUs 'BepInEx\plugins'
$dest = Join-Path $plugins 'EHR.dll'
if (Test-Path -LiteralPath $plugins) {
    $needCopy = $true
    if (Test-Path -LiteralPath $dest) {
        $needCopy = ((Get-Item -LiteralPath $dest).LastWriteTime -lt (Get-Item -LiteralPath $dll).LastWriteTime)
    }
    if ($needCopy) {
        try {
            Copy-Item -LiteralPath $dll -Destination $dest -Force -ErrorAction Stop
            Ok "Copiado para: $dest"
        }
        catch {
            Warn "Nao consegui copiar para $dest (jogo aberto?). Copie manualmente."
        }
    }
    else {
        Ok "Ja esta atualizado em: $dest"
    }
}
else {
    Warn "Pasta $plugins nao existe. Copie o EHR.dll manualmente para a pasta BepInEx\plugins do jogo."
}

Start-Process explorer.exe -ArgumentList ('/select,"' + $dll + '"')
Write-Host ''
Write-Host 'Pronto. Abra o Among Us. Se ele fechar ao iniciar, avise aqui e mande o arquivo BepInEx\LogOutput.log.' -ForegroundColor Cyan