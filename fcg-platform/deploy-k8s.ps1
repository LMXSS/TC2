# Aplica no cluster local a infraestrutura (RabbitMQ + Mailpit) e os manifestos das 4 APIs.
#
# Os manifestos das APIs vivem na pasta k8s/ da RAIZ de cada repositorio de API
# (nao neste repo), entao o script precisa saber onde eles estao.
#
# Uso:
#   .\deploy-k8s.ps1                                  # detecta automaticamente
#   .\deploy-k8s.ps1 -ReposRoot D:\PosGrad\fcg-repos  # pasta com os 4 repos de API
#   .\deploy-k8s.ps1 -Delete                          # remove tudo do cluster
#
# Pre-requisitos:
#   - Kubernetes habilitado (Docker Desktop -> Settings -> Kubernetes)
#   - Imagens fcg/*:latest ja buildadas (.\build-images.ps1)
#   - jwt-key igual nos secret.yaml de Users e Catalog (senao o token e rejeitado)

param(
    [string]$ReposRoot,
    [switch]$Delete
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
    Write-Host "kubectl nao encontrado no PATH." -ForegroundColor Red
    exit 1
}

$services = @(
    @{ Api = "Users.Api";         Repo = "fcg-users-api" },
    @{ Api = "Catalog.Api";       Repo = "fcg-catalog-api" },
    @{ Api = "Payments.Api";      Repo = "fcg-payments-api" },
    @{ Api = "Notifications.Api"; Repo = "fcg-notifications-api" }
)

# Descobre a pasta k8s/ de cada API: repos isolados (k8s na raiz do repo, L1)
# ou monorepo (k8s dentro da pasta da API).
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
        $k8s = if ($c.Mode -eq "repos") { Join-Path $c.Root "$($svc.Repo)\k8s" } else { Join-Path $c.Root "$($svc.Api)\k8s" }
        if (-not (Test-Path $k8s)) { $ok = $false; break }
    }
    if ($ok) { $source = $c; break }
}

if (-not $source) {
    Write-Host "Nao encontrei as pastas k8s/ das APIs." -ForegroundColor Red
    Write-Host "Clone os 4 repos lado a lado e aponte a pasta que os contem:" -ForegroundColor Yellow
    Write-Host "  .\deploy-k8s.ps1 -ReposRoot C:\caminho\para\os\repos" -ForegroundColor Gray
    Write-Host "Esperado: <ReposRoot>\fcg-users-api\k8s\ (e os outros 3)" -ForegroundColor Gray
    exit 1
}

$rootPath = (Resolve-Path $source.Root).Path
$verb = if ($Delete) { "delete" } else { "apply" }

Write-Host "Origem dos manifestos: $rootPath  (modo: $($source.Mode))" -ForegroundColor Cyan
Write-Host "Operacao: kubectl $verb" -ForegroundColor Cyan

# Infra primeiro: as APIs dependem de rabbitmq e mailpit por nome de Service.
$infra = @(
    (Join-Path $PSScriptRoot "k8s\rabbitmq.yaml"),
    (Join-Path $PSScriptRoot "k8s\mailpit.yaml")
)

foreach ($file in $infra) {
    Write-Host ""
    Write-Host ">> kubectl $verb -f $(Split-Path -Leaf $file)" -ForegroundColor Cyan
    & kubectl $verb -f $file
    if ($LASTEXITCODE -ne 0 -and -not $Delete) { exit $LASTEXITCODE }
}

foreach ($svc in $services) {
    $k8s = if ($source.Mode -eq "repos") { Join-Path $rootPath "$($svc.Repo)\k8s" } else { Join-Path $rootPath "$($svc.Api)\k8s" }

    Write-Host ""
    Write-Host ">> [$($svc.Api)] kubectl $verb -f $k8s" -ForegroundColor Cyan
    & kubectl $verb -f $k8s
    if ($LASTEXITCODE -ne 0 -and -not $Delete) { exit $LASTEXITCODE }
}

if ($Delete) {
    Write-Host ""
    Write-Host "[OK] Recursos removidos do cluster." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[OK] Manifestos aplicados. Acompanhe os pods:" -ForegroundColor Green
Write-Host "  kubectl get pods -w" -ForegroundColor Gray
Write-Host ""
kubectl get pods
Write-Host ""
Write-Host "Acesso (port-forward, um por terminal):" -ForegroundColor Gray
Write-Host "  kubectl port-forward svc/users-api 5101:80" -ForegroundColor Gray
Write-Host "  kubectl port-forward svc/catalog-api 5102:80" -ForegroundColor Gray
Write-Host "  kubectl port-forward svc/mailpit 8025:8025" -ForegroundColor Gray
