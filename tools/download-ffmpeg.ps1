# Baixa o ffmpeg (build "release essentials" da gyan.dev) e gera tools\ffmpeg.exe.gz, que o LanCast embute.
# Uso: powershell -File tools\download-ffmpeg.ps1 [-Force]
#   -Force  baixa de novo mesmo que tools\ffmpeg.exe já exista.
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # a barra de progresso do Invoke-WebRequest deixa o download muito mais lento no PowerShell 5.1
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$tools = $PSScriptRoot
$exe = Join-Path $tools 'ffmpeg.exe'
$gz = Join-Path $tools 'ffmpeg.exe.gz'
$url = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

if ($Force -or -not (Test-Path $exe)) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("lancast-ffmpeg-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        $zip = Join-Path $tmp 'ffmpeg.zip'
        Write-Host "Baixando $url ..."
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing

        $expected = (Invoke-WebRequest -Uri "$url.sha256" -UseBasicParsing).Content
        if ($expected -is [byte[]]) { $expected = [Text.Encoding]::ASCII.GetString($expected) }
        $expected = $expected.Trim().Split(' ')[0]
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
        if ($actual -ne $expected) { throw "SHA-256 do ffmpeg não confere (esperado $expected, obtido $actual)." }

        Expand-Archive -Path $zip -DestinationPath $tmp
        $found = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Where-Object { $_.Directory.Name -eq 'bin' } | Select-Object -First 1
        if (-not $found) { throw 'ffmpeg.exe não encontrado no zip.' }
        Copy-Item $found.FullName $exe -Force
    }
    finally {
        Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
    }
}
else {
    Write-Host "Usando $exe existente (use -Force para baixar de novo)."
}

Write-Host 'Compactando para tools\ffmpeg.exe.gz ...'
$in = [IO.File]::OpenRead($exe)
try {
    $out = [IO.File]::Create($gz)
    try {
        $zipStream = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
        try { $in.CopyTo($zipStream) } finally { $zipStream.Dispose() }
    }
    finally { $out.Dispose() }
}
finally { $in.Dispose() }

Write-Host 'Pronto: tools\ffmpeg.exe.gz'
