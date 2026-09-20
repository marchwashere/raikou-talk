$ErrorActionPreference='Stop'
$webRoot=$PSScriptRoot
$appRoot=$webRoot
$readerRoot=Join-Path $webRoot 'engine'
$buildRoot=Join-Path $webRoot 'build'
$publicRoot=Join-Path $webRoot 'public'
$verifyRoot=Join-Path $webRoot 'verification'
New-Item -ItemType Directory -Force -Path $buildRoot,$publicRoot,$verifyRoot | Out-Null
$frameworkRoot=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$model=([IO.File]::ReadAllText((Join-Path $readerRoot 'data\model-id.txt'))).Trim()
$info=Join-Path $buildRoot 'BuildInfo.cs'
[IO.File]::WriteAllText($info,'namespace HgssStrategyReader { static class BuildInfo { public const string ModelId="'+$model+'"; } }')
$exe=Join-Path $buildRoot 'ExportWeb.exe'
$compilerArgs=@('/nologo','/target:exe','/platform:x64','/optimize+','/utf8output',('/out:'+$exe))
foreach($reference in @('System.dll','System.Core.dll','System.Numerics.dll','System.Drawing.dll')) { $compilerArgs+='/reference:'+(Join-Path $frameworkRoot $reference) }
foreach($resource in @('game-data.json','movement-timings.json','emotes.json')) { $compilerArgs+='/resource:'+(Join-Path $readerRoot ('data\'+$resource))+','+$resource }
foreach($source in @('GameModel.cs','Json.cs','ResponseCatalog.cs','StrategyEngine.cs')) { $compilerArgs+=Join-Path $readerRoot $source }
$compilerArgs+=$info
$compilerArgs+=Join-Path $webRoot 'ExportWeb.cs'
& (Join-Path $frameworkRoot 'csc.exe') $compilerArgs
if($LASTEXITCODE -ne 0) { throw 'Web exporter did not compile' }
& $exe $appRoot $publicRoot (Join-Path $verifyRoot 'native-parity.json')
if($LASTEXITCODE -ne 0) { throw 'Native policy verification failed' }
foreach($name in @('index.html','app.mjs','reader-state.mjs','styles.css')) { Copy-Item -LiteralPath (Join-Path $webRoot ('src\'+$name)) -Destination (Join-Path $publicRoot $name) -Force }
Write-Host ('Built '+$publicRoot)
$distRoot=Join-Path $appRoot 'dist'
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
$package=Join-Path $distRoot 'Raikou Manip Web.zip'
$packageItems=@($publicRoot)
foreach($name in @('README.md','Caddyfile.example','Caddyfile.subpath.example')) { $packageItems+=Join-Path $webRoot $name }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageStream=[IO.File]::Create($package)
$archive=New-Object IO.Compression.ZipArchive($packageStream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($item in $packageItems) {
        if(Test-Path -LiteralPath $item -PathType Container) { $files=Get-ChildItem -LiteralPath $item -File -Recurse }
        else { $files=@(Get-Item -LiteralPath $item) }
        foreach($file in $files) {
            $entryName=$file.FullName.Substring($webRoot.Length+1).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entryName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $archive.Dispose(); $packageStream.Dispose() }
Write-Host ('Packaged '+$package)
