<#
.SYNOPSIS
  Stores MasidBaha's local secrets in the .NET user-secrets store (outside the repo).

.DESCRIPTION
  - Prompts for the PostgreSQL connection string (Enter = the docker-compose database).
  - Generates the first back-office admin login, a session-cookie signing key and a fresh VAPID key pair (web push).
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

# The first SuperAdmin, created on the API's first start if no SuperAdmin exists yet
$adminEmail = Read-Host 'Admin email for the back-office login (press Enter for admin@masidbaha.local)'
if ([string]::IsNullOrWhiteSpace($adminEmail)) { $adminEmail = 'admin@masidbaha.local' }
$bytes = New-Object byte[] 18
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$adminPassword = [Convert]::ToBase64String($bytes).Replace('+', '-').Replace('/', '_')
Set-Secret 'AdminAuth:InitialAdmin:Email' $adminEmail
Set-Secret 'AdminAuth:InitialAdmin:Password' $adminPassword

# Signs the anonymous session cookie, so a visitor can't forge another visitor's id
$signingBytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($signingBytes)
Set-Secret 'Security:SessionSigningKey' ([Convert]::ToBase64String($signingBytes))

$vapid = npx --yes web-push generate-vapid-keys --json | ConvertFrom-Json
Set-Secret 'Vapid:PublicKey' $vapid.publicKey
Set-Secret 'Vapid:PrivateKey' $vapid.privateKey
Set-Secret 'Vapid:Subject' 'mailto:admin@masidbaha.local'

Write-Host "Done. Secrets saved to the user-secrets store."
Write-Host "Back-office login (save it, then change the password after signing in):"
Write-Host "  email:    $adminEmail"
Write-Host "  password: $adminPassword"
