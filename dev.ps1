# Tarefas de desenvolvimento do LanCast. Uso: .\dev.ps1 <comando>  (ou dev.cmd <comando>, que ignora a política de execução)
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$host_ = Join-Path $root 'src\Host'
$csproj = Join-Path $host_ 'LanCastHost.csproj'
$ffmpegGz = Join-Path $root 'tools\ffmpeg.exe.gz'

function Invoke-Native([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe terminou com código $LASTEXITCODE." }
}

function Assert-Sdk {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET SDK não encontrado. Instale o .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'
    }
    $sdks = & dotnet --list-sdks | ForEach-Object { [int]($_.Split('.')[0]) }
    if (-not ($sdks | Where-Object { $_ -ge 8 })) {
        throw 'É preciso o .NET SDK 8 ou mais novo: https://dotnet.microsoft.com/download/dotnet/8.0'
    }
}

function Install-Ffmpeg {
    if ($Force -or -not (Test-Path $ffmpegGz)) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'tools\download-ffmpeg.ps1'))
        if ($Force) { $arguments += '-Force' }
        Invoke-Native 'powershell' $arguments
    }
    else {
        Write-Host 'ffmpeg ok (tools\ffmpeg.exe.gz). Use -Force para baixar de novo.'
    }
}

function Get-AppVersion {
    ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}

function Find-Iscc {
    $candidates = @(
        (Join-Path $root 'tools\InnoSetup\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw 'ISCC.exe (Inno Setup 6) não encontrado.' }
    $found
}

switch ($Command) {
    'setup' {
        Assert-Sdk
        Install-Ffmpeg
        Invoke-Native 'dotnet' @('restore', (Join-Path $root 'LanCast.sln'))
        Write-Host "`nTudo pronto. Rode .\dev.ps1 run"
    }
    'build' {
        Invoke-Native 'dotnet' @('build', (Join-Path $root 'LanCast.sln'))
    }
    'run' {
        # O app ainda mira o .NET 6; isto deixa rodar no runtime mais novo instalado.
        $env:DOTNET_ROLL_FORWARD = 'Major'
        Invoke-Native 'dotnet' @('run', '--project', $host_)
    }
    'test' {
        Invoke-Native 'dotnet' @('run', '--project', (Join-Path $root 'tests\UpdateChecks'))
    }
    'site' {
        $site = Join-Path $root 'website'
        if (Get-Command python -ErrorAction SilentlyContinue) {
            Write-Host 'Landing page em http://127.0.0.1:4173 (Ctrl+C para parar)'
            Invoke-Native 'python' @('-m', 'http.server', '4173', '--bind', '127.0.0.1', '--directory', $site)
        }
        else {
            Write-Host 'Python não encontrado; abrindo website\index.html direto no navegador.'
            Start-Process (Join-Path $site 'index.html')
        }
    }
    'publish' {
        $version = Get-AppVersion
        $portable = Join-Path $root 'dist\portable'

        Write-Host '=== 1/2: modo portátil  (dist\portable\LanCast.exe)'
        Invoke-Native 'dotnet' @('publish', $host_, '-c', 'Release', '-r', 'win-x64', '--self-contained',
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
            '-o', $portable)
        Remove-Item (Join-Path $portable '*.pdb'), (Join-Path $portable 'web.config') -ErrorAction SilentlyContinue

        Write-Host "`n=== 2/2: instalador  (dist\installer\LanCast-Setup-$version.exe)"
        Invoke-Native (Find-Iscc) @("/DAppVersion=$version", (Join-Path $root 'installer\LanCast.iss'))

        Copy-Item (Join-Path $portable 'LanCast.exe') (Join-Path $portable "LanCast-Portable-$version.exe") -Force
        Write-Host "`nPronto:"
        Write-Host "  Portátil:   dist\portable\LanCast-Portable-$version.exe"
        Write-Host '  Instalador: dist\installer\'
    }
    default {
        Write-Host @'
Uso: .\dev.ps1 <comando> [-Force]

  setup     confere o .NET SDK, baixa o ffmpeg (tools\ffmpeg.exe.gz) e restaura os pacotes
  build     compila o app e os testes
  run       roda o app
  test      roda os testes do atualizador (tests\UpdateChecks)
  site      serve a landing page em http://127.0.0.1:4173
  publish   gera o portátil e o instalador em dist\ (o mesmo que publish.bat)

  -Force    no setup, baixa o ffmpeg de novo
'@
    }
}
