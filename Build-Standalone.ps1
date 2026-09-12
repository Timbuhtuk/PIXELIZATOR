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
TESSERA / WINDOWS x64

Run Tessera.exe. No .NET installation or internet connection is required.
You can move the executable to any folder.

Tessera prepares pixel art and icons. It began as a way to correct uneven
pixel grids and inconsistent pixel sizes in AI-generated images.
Its name refers to an individual piece of a mosaic.

Open images with the Open button, Ctrl+O or drag and drop.
The home screen keeps your source library and result histories.
The editor offers downscaling, palettes, grid alignment and cell reduction.
Save exports the selected result to a separate file.

The icon banner opens ICO creation: choose an image and several sizes,
inspect the previews, then save them together in one icon file.
You can also use a source or result from the editor. Optional solid-color
background removal supports color selection and adjustable tolerance.
It does not segment objects from complex photographic backgrounds.
The application interface currently uses Russian labels.

Your library stays at %LOCALAPPDATA%\Pixelizator\Library.
This historical path preserves your existing images and histories.
Copy that folder separately when moving your library to another computer.

Updates: https://github.com/Timbuhtuk/PIXELIZATOR/releases/latest
'@
    [IO.File]::WriteAllText((Join-Path $output 'README.txt'), $guide, [Text.UTF8Encoding]::new($true))
    $archive = Join-Path $projectRoot 'artifacts\Tessera-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $output 'Tessera.exe'),(Join-Path $output 'README.txt') -DestinationPath $archive -Force
    $cliArchive = Join-Path $projectRoot 'artifacts\Tessera-cli-win-x64.zip'
    Compress-Archive -LiteralPath (Join-Path $projectRoot 'artifacts\cli\tessera.exe') -DestinationPath $cliArchive -Force
    $hash = Get-FileHash -LiteralPath (Join-Path $output 'Tessera.exe') -Algorithm SHA256
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\Tessera-win-x64.sha256'), "$($hash.Hash)  Tessera.exe`n")
    $checksums = foreach ($file in @((Join-Path $output 'Tessera.exe'), $archive, $cliArchive)) {
        $checksum = Get-FileHash -LiteralPath $file -Algorithm SHA256
        "$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"
    }
    [IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts\SHA256SUMS.txt'), ($checksums -join "`n") + "`n")
    Write-Output "EXE: $(Join-Path $output 'Tessera.exe')"
    Write-Output "CLI: $(Join-Path $projectRoot 'artifacts\cli\tessera.exe')"
    Write-Output "ZIP: $archive"
    Write-Output "CLI ZIP: $cliArchive"
}
finally { Pop-Location }
