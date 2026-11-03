# Builda as 4 imagens fcg/*:latest usadas pelo docker-compose.yml e pelos manifestos k8s.
#
# O compose deste repositorio nao builda a partir do codigo-fonte: cada API vive
# em seu proprio repositorio Git. Este script encontra esses repos e builda cada
# imagem a partir da RAIZ do repo correspondente.
#
# Uso:
#   .\build-images.ps1                                  # detecta automaticamente
#   .\build-images.ps1 -ReposRoot D:\PosGrad\fcg-repos  # pasta com os 4 repos de API
#   .\build-images.ps1 -NoCache
#
# Layouts aceitos (nesta ordem):
#   1. Repos isolados:  <ReposRoot>\fcg-users-api\Users.Api\Dockerfile
#   2. Monorepo:        <raiz>\microservices\Users.Api\Dockerfile

param(
    [string]$ReposRoot,
    [switch]$NoCache
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "Docker nao encontrado no PATH. Instale o Docker Desktop e reabra o terminal." -ForegroundColor Red
    exit 1
}

$services = @(
    @{ Api = "Users.Api";         Repo = "fcg-users-api";         Tag = "fcg/users-api:latest" },
    @{ Api = "Catalog.Api";       Repo = "fcg-catalog-api";       Tag = "fcg/catalog-api:latest" },
    @{ Api = "Payments.Api";      Repo = "fcg-payments-api";      Tag = "fcg/payments-api:latest" },
    @{ Api = "Notifications.Api"; Repo = "fcg-notifications-api"; Tag = "fcg/notifications-api:latest" }
)

# Descobre onde esta o codigo-fonte de cada API.
$parent = Split-Path -Parent $PSScriptRoot

$candidates = @()
if ($ReposRoot) { $candidates += @{ Mode = "repos"; Root = $ReposRoot } }
$candidates += @{ Mode = "monorepo"; Root = (Join-Path $parent "microservices") }
$candidates += @{ Mode = "repos";    Root = $parent }

$source = $null
foreach ($c in $candidates) {
    if (-not (Test-Path $c.Root)) { continue }

    $ok = $true
    foreach ($svc in $services) {
        $context = if ($c.Mode -eq "repos") { Join-Path $c.Root $svc.Repo } else { $c.Root }
        if (-not (Test-Path (Join-Path $context "$($svc.Api)\Dockerfile"))) { $ok = $false; break }
    }
    if ($ok) { $source = $c; break }
}

if (-not $source) {
    Write-Host "Nao encontrei o codigo-fonte das APIs." -ForegroundColor Red
    Write-Host "Clone os 4 repos lado a lado e aponte a pasta que os contem:" -ForegroundColor Yellow
    Write-Host "  .\build-images.ps1 -ReposRoot C:\caminho\para\os\repos" -ForegroundColor Gray
    Write-Host "Esperado: <ReposRoot>\fcg-users-api\Users.Api\Dockerfile (e os outros 3)" -ForegroundColor Gray
    exit 1
}

$rootPath = (Resolve-Path $source.Root).Path
Write-Host "Origem: $rootPath  (modo: $($source.Mode))" -ForegroundColor Cyan

foreach ($svc in $services) {
    $context = if ($source.Mode -eq "repos") { Join-Path $rootPath $svc.Repo } else { $rootPath }

    $buildArgs = @("build", "-f", "$($svc.Api)/Dockerfile", "-t", $svc.Tag)
    if ($NoCache) { $buildArgs += "--no-cache" }
    $buildArgs += "."

    Write-Host ""
    Write-Host ">> [$($svc.Api)] docker $($buildArgs -join ' ')" -ForegroundColor Cyan
    Write-Host "   contexto: $context" -ForegroundColor DarkGray

    Push-Location $context
    try {
        & docker @buildArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERRO] Falha ao buildar $($svc.Tag)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "[OK] Imagens prontas:" -ForegroundColor Green
docker images --filter "reference=fcg/*" --format "  {{.Repository}}:{{.Tag}}  {{.Size}}"
Write-Host ""
Write-Host "Proximos passos:" -ForegroundColor Gray
Write-Host "  Docker:     docker compose up -d" -ForegroundColor Gray
Write-Host "  Kubernetes: .\deploy-k8s.ps1 -ReposRoot <pasta-dos-repos>" -ForegroundColor Gray
