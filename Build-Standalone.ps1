param(
    [switch]$SkipChecks,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source

function Invoke-Dotnet {
    & $script:dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: exit $LASTEXITCODE" }
}

Push-Location -LiteralPath $projectRoot
try {
    Invoke-Dotnet build Pixelizator.sln -c Release "-p:Version=$Version" --nologo
    if (!$SkipChecks) {
        Invoke-Dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --ui --invariants
        Invoke-Dotnet run --project Pixelizator.Cli.Tests -c Release --no-build
    }
    Invoke-Dotnet publish DomainColorTest -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" --nologo
    Invoke-Dotnet publish Pixelizator.Cli -c Release '-p:PublishProfile=win-x64' "-p:Version=$Version" --nologo
    $output = Join-Path $projectRoot 'artifacts\standalone'
    $guide = @'
PIXELIZATOR / WINDOWS x64

Запустите Pixelizator.exe. Устанавливать .NET не требуется.
EXE можно перенести в любую папку и запускать без подключения к интернету.

Добавление изображений: кнопка «Открыть», Ctrl+O или перетаскивание файлов.
Главная: библиотека исходников и сохранённых результатов.
Второй баннер открывает создание ICO: перетащите изображение, выберите размеры
и нажмите «Сохранить ICO…». Можно взять исходник или результат редактора.
«Удалить фон» делает однотонный фон прозрачным; цвет и допуск настраиваются.
Редактор: уменьшение, палитры, выравнивание и сжатие пиксельной сетки.
«Сохранить» экспортирует выбранный результат в отдельный файл.

Библиотека автоматически хранится в %LOCALAPPDATA%\Pixelizator\Library.
При переносе на другой компьютер скопируйте эту папку отдельно,
если хотите перенести свои изображения и историю.
'@
    [IO.File]::WriteAllText((Join-Path $output 'README.txt'), $guide, [Text.UTF8Encoding]::new($true))
    $archive = Join-Path $projectRoot 'artifacts\Pixelizator-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $output 'Pixelizator.exe'),(Join-Path $output 'README.txt') -DestinationPath $archive -Force
    $cliArchive = Join-Path $projectRoot 'artifacts\Pixelizator-cli-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $projectRoot 'artifacts\cli\pixelizator.exe') -DestinationPath $cliArchive -Force
    $hash = Get-FileHash -LiteralPath (Join-Path $output 'Pixelizator.exe') -Algorithm SHA256
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\Pixelizator-win-x64.sha256'), "$($hash.Hash)  Pixelizator.exe`n")
    $checksums = foreach ($file in @((Join-Path $output 'Pixelizator.exe'), $archive, $cliArchive)) {
        $checksum = Get-FileHash -LiteralPath $file -Algorithm SHA256
        "$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
    }
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\SHA256SUMS.txt'), ($checksums -join "`n") + "`n")
    Write-Output "EXE: $(Join-Path $output 'Pixelizator.exe')"
    Write-Output "CLI: $(Join-Path $projectRoot 'artifacts\cli\pixelizator.exe')"
    Write-Output "ZIP: $archive"
    Write-Output "CLI ZIP: $cliArchive"
}
finally { Pop-Location }
