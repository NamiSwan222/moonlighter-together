# Builds release\MoonlighterTogether-<version>.zip: BepInEx 5.4.23.6 (official zip, SHA-256 checked) + the plugin +
# readme and licences. Nothing from the game is included. Run: powershell -File tools\make-release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bepZip = Join-Path $root 'tools\downloads\bepinex\BepInEx_win_x64_5.4.23.6.zip'
$bepSha = '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'
if (-not (Test-Path $bepZip)) { throw "missing $bepZip (download it from github.com/BepInEx/BepInEx releases v5.4.23.6)" }
if ((Get-FileHash $bepZip -Algorithm SHA256).Hash.ToLower() -ne $bepSha) { throw 'BepInEx zip hash mismatch' }

Push-Location (Join-Path $root 'src\MoonlighterTogether')
dotnet build -c Release -nologo -v q -p:DeployToGame=false | Out-Host
Pop-Location
$bin = Join-Path $root 'src\MoonlighterTogether\bin\Release'
$version = (Get-Item (Join-Path $bin 'MoonlighterTogether.dll')).VersionInfo.ProductVersion -replace '\+.*$', ''

$stage = Join-Path $root 'release\staging'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
Expand-Archive -Path $bepZip -DestinationPath $stage
$plug = Join-Path $stage 'BepInEx\plugins\MoonlighterTogether'
New-Item -ItemType Directory -Force $plug | Out-Null
Copy-Item (Join-Path $bin 'MoonlighterTogether.dll'), (Join-Path $bin 'LiteNetLib.dll') $plug
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'MoonlighterTogether-README.md')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $plug 'LICENSE.txt')
@"
Third-party components in this package:
- BepInEx 5.4.23.6 (https://github.com/BepInEx/BepInEx), LGPL-2.1, unmodified official build.
- HarmonyX (inside BepInEx), MIT.
- LiteNetLib 1.3.1 (https://github.com/RevenantX/LiteNetLib), MIT.
"@ | Set-Content -Encoding utf8 (Join-Path $plug 'THIRD-PARTY.txt')

# Allow-list: anything else in the staging folder is a packaging mistake.
$allowed = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt', 'MoonlighterTogether-README.md')
Get-ChildItem $stage -File | Where-Object { $allowed -notcontains $_.Name } | ForEach-Object { throw "unexpected file in package: $($_.Name)" }
Get-ChildItem $stage -Recurse -File | Where-Object { $_.Extension -in '.bat', '.cmd', '.ps1', '.exe' } | ForEach-Object { throw "no scripts or executables in the package: $($_.FullName)" }

$zip = Join-Path $root "release\MoonlighterTogether-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item -Recurse -Force $stage
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path -Leaf $zip)" | Set-Content -Encoding ascii "$zip.sha256"
"Built $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB) sha256 $hash"
