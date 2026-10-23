<#
.SYNOPSIS
  Stores MasidBaha's local secrets in the .NET user-secrets store (outside the repo).

.DESCRIPTION
  - Prompts for the PostgreSQL connection string (Enter = the docker-compose database).
  - Generates a fresh admin API key and a fresh VAPID key pair (web push).
  Run from the repo root:  pwsh ./scripts/init-dev-secrets.ps1
  Requires the .NET SDK and Node.js (for `npx web-push`).
#>
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\backend\MasidBaha.WebAPI\MasidBaha.WebAPI.csproj'

function Set-Secret([string]$key, [string]$value) {
    dotnet user-secrets set $key $value --project $project | Out-Null
}

$localDocker = 'Host=localhost;Port=5433;Database=masidbaha;Username=masidbaha;Password=masidbaha_dev'
$connection = Read-Host "PostgreSQL connection string (press Enter for the local docker-compose database)"
if ([string]::IsNullOrWhiteSpace($connection)) { $connection = $localDocker }
Set-Secret 'ConnectionStrings:MasidBahaDb' $connection

$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$adminKey = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
Set-Secret 'Admin:ApiKey' $adminKey

$vapid = npx --yes web-push generate-vapid-keys --json | ConvertFrom-Json
Set-Secret 'Vapid:PublicKey' $vapid.publicKey
Set-Secret 'Vapid:PrivateKey' $vapid.privateKey
Set-Secret 'Vapid:Subject' 'mailto:admin@masidbaha.local'

Write-Host "Done. Secrets saved to the user-secrets store."
Write-Host "Admin panel key (save it somewhere safe, you need it to log in): $adminKey"
