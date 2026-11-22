<#
.SYNOPSIS
  Compile le plugin et génère un dépôt Jellyfin auto-hébergé (manifest.json + zip).

.EXAMPLE
  .\scripts\build-local.ps1 -Version 1.0.0 -BaseUrl http://192.168.99.250/profile-picker

  Produit .\repo\ :
    manifest.json                     -> URL à ajouter dans Jellyfin (Plugins > Dépôts)
    profile-picker_1.0.0.1.zip        -> paquet Jellyfin 12.x
  Copier tout le contenu de .\repo\ dans le dossier web servi à -BaseUrl.
  Les versions précédentes présentes dans .\repo\manifest.json sont conservées.
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,          # ex. 1.0.0
    [Parameter(Mandatory = $true)][string]$BaseUrl,          # ex. http://192.168.99.250/profile-picker
    [string]$Changelog = "Version $Version",
    [switch]$Include1011                                     # ajoute aussi un build Jellyfin 10.11
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$proj    = Join-Path $root 'src\Jellyfin.Plugin.ProfilePicker\Jellyfin.Plugin.ProfilePicker.csproj'
$repo    = Join-Path $root 'repo'
$build   = Join-Path $root 'build'
$guid    = '28e96038-2eff-428b-bdb2-c2115837e144'
$BaseUrl = $BaseUrl.TrimEnd('/')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "SDK .NET introuvable. Installe-le : winget install Microsoft.DotNet.SDK.10"
}
New-Item -ItemType Directory -Force -Path $repo | Out-Null

$targets = @(@{ Jf = '12'; Rev = 1; Abi = '12.0.0.0' })
if ($Include1011) { $targets += @{ Jf = '10.11'; Rev = 0; Abi = '10.11.0.0' } }

$newVersions = @()
foreach ($t in $targets) {
    $ver = "$Version.$($t.Rev)"
    $out = Join-Path $build $t.Jf
    Write-Host "==> Build Jellyfin $($t.Jf) (v$ver)" -ForegroundColor Cyan
    & dotnet build $proj -c Release "-p:JellyfinTarget=$($t.Jf)" "-p:Version=$ver" "-p:AssemblyVersion=$ver" "-p:FileVersion=$ver" -o $out
    if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué (Jellyfin $($t.Jf))." }

    $zipName = "profile-picker_$ver.zip"
    $zip = Join-Path $repo $zipName
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $out 'Jellyfin.Plugin.ProfilePicker.dll') -DestinationPath $zip
    $md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLowerInvariant()

    $newVersions += [ordered]@{
        version   = $ver
        changelog = $Changelog
        targetAbi = $t.Abi
        sourceUrl = "$BaseUrl/$zipName"
        checksum  = $md5
        timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
}

# Fusion avec les versions déjà publiées
$manifestPath = Join-Path $repo 'manifest.json'
$old = @()
if (Test-Path $manifestPath) {
    $existing = @(Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json)
    $p = $existing | Where-Object { $_.guid -eq $guid } | Select-Object -First 1
    if ($p) {
        $newIds = $newVersions | ForEach-Object { $_.version }
        $old = @($p.versions | Where-Object { $newIds -notcontains $_.version })
    }
}
$all = @($newVersions) + @($old) | Sort-Object -Property @{ Expression = { [version]$_.version } } -Descending

$plugin = [ordered]@{
    guid        = $guid
    name        = 'Profile Picker'
    overview    = "Écran « Qui regarde ? » avec avatars prédéfinis"
    description = "Connexion avec un compte foyer puis choix du profil (avatars, PIN), façon plateformes de streaming."
    owner       = 'Alex'
    category    = 'General'
    imageUrl    = "$BaseUrl/logo.png"
    versions    = @($all)
}
$json = ConvertTo-Json -InputObject @($plugin) -Depth 6
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Copy-Item (Join-Path $root 'docs\logo.png') (Join-Path $repo 'logo.png') -Force

Write-Host ""
Write-Host "Dépôt prêt dans : $repo" -ForegroundColor Green
Get-ChildItem $repo | Format-Table Name, Length -AutoSize
Write-Host "1. Copie le contenu de $repo vers le dossier servi à $BaseUrl"
Write-Host "2. Jellyfin > Plugins > Dépôts > + : $BaseUrl/manifest.json"
