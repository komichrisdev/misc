param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
if (!(Test-Path $compiler)) { throw 'Beholder requires Windows x64 with .NET Framework 4.8.' }
$dist = Join-Path $root 'dist'
$evidence = Join-Path $root 'evidence'
New-Item -ItemType Directory -Path $dist, $evidence -Force | Out-Null
$refs = @('/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Xaml.dll')
foreach ($name in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) {
    $refs += '/reference:' + (Join-Path $wpf $name)
}
$common = @('/nologo', '/platform:anycpu', '/optimize+', '/warn:4', ('/win32manifest:' + (Join-Path $root 'app.manifest')), ('/resource:' + (Join-Path $root 'beholder.ico') + ',Beholder.Icon.ico')) + $refs
$common += '/resource:' + (Join-Path $root 'third_party\libwebp\dwebp.exe.gz') + ',Beholder.WebP.gz'
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$app = Join-Path $dist 'Beholder v3.exe'
& $compiler @common '/target:winexe' '/main:Beholder.Program' ('/out:' + $app) ('/win32icon:' + (Join-Path $root 'beholder.ico')) @sources
if ($LASTEXITCODE -ne 0) { throw "App compilation failed with exit code $LASTEXITCODE" }
Write-Output "Built $app ($((Get-Item $app).Length) bytes)"
if ($Test) {
    $tester = Join-Path $evidence 'Beholder.Tests.exe'
    & $compiler @common '/target:exe' '/main:Beholder.Tests' '/reference:System.Xml.Linq.dll' ('/out:' + $tester) @sources (Join-Path $root 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE" }
    & $tester $evidence 2>&1 | Tee-Object -FilePath (Join-Path $evidence 'test.log')
    if ($LASTEXITCODE -ne 0) { throw "Regression tests failed with exit code $LASTEXITCODE; see evidence\tests.xml" }
}
