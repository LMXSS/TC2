# FCG — Catalog API

Microsserviço de **catálogo de jogos e biblioteca do usuário** da plataforma FIAP Cloud Games (FCG).

Responsabilidades:

- CRUD de jogos (restrito ao papel **Administrador**) e listagem pública.
- Biblioteca do usuário autenticado.
- **Início do fluxo de compra**: `POST /api/users/me/library/games/{gameId}` publica **`OrderPlacedEvent`** e responde **202 Accepted** — a compra é assíncrona.
- Consome **`PaymentProcessedEvent`**: ao receber um pagamento **aprovado**, adiciona o jogo à biblioteca do usuário.

Este repositório é **autocontido**: builda, containeriza e faz deploy sem depender de nenhum outro repositório da plataforma.

---

## Estrutura

```
fcg-catalog-api/
├── k8s/                       Manifestos Kubernetes (Deployment, Service, ConfigMap, Secret)
├── Catalog.Api/               Código da API + Dockerfile
├── FCG.Messaging.Contracts/   Contratos de evento compartilhados entre os microsserviços
├── Catalog.Api.sln
└── README.md
```

> `FCG.Messaging.Contracts` é a **cópia** dos contratos de mensagem da plataforma. Os quatro microsserviços carregam a mesma cópia: alterar um evento exige replicar a mudança nos quatro repositórios.

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **RabbitMQ** acessível.
- **Users API** no ar — é quem emite o token JWT usado aqui. `Jwt__Key`, `Jwt__Issuer` e `Jwt__Audience` **precisam ser idênticas** nos dois serviços, senão o token é rejeitado.
- Para exercitar a compra de ponta a ponta: **Payments API** (processa o pagamento) e **Notifications API** (envia o e-mail).

Subir um RabbitMQ local:

```powershell
docker run -d --name fcg-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management-alpine
```

---

## Executar

### Local (.NET SDK)

Da **raiz deste repositório**:

```powershell
dotnet run --project Catalog.Api
```

Swagger (Development): http://localhost:5102/swagger — clique em **Authorize** e informe o token `Bearer` emitido pelo Users API.

As migrations são aplicadas automaticamente na subida.

### Docker

Da **raiz deste repositório** (o contexto do build é a raiz, pois o Dockerfile copia a API **e** os contratos):

```powershell
docker build -f Catalog.Api/Dockerfile -t fcg/catalog-api:latest .
docker run -p 5102:8080 `
  -e RabbitMq__Host=host.docker.internal `
  -e Jwt__Key="CHANGE_ME_DEV_KEY_MIN_32_CHARS_LONG!!" `
  fcg/catalog-api:latest
```

> No Linux, troque `host.docker.internal` pelo IP do host para alcançar o RabbitMQ.

A imagem `fcg/catalog-api:latest` é a que o repositório de orquestração (`fcg-platform`) espera encontrar no `docker-compose`, e a mesma referenciada pelo `k8s/deployment.yaml`.

---

## Variáveis de ambiente

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `ConnectionStrings__DefaultConnection` | Banco SQLite do catálogo e da biblioteca. No container, aponte para o volume: `Data Source=/app/data/catalog.db`. | `Data Source=catalog.db` |
| `Jwt__Key` | Chave simétrica de validação, **mínimo 32 caracteres**. ⚠️ **Deve ser idêntica à do Users API** — divergência aqui é a causa mais comum de `401` neste serviço. | *(valor de dev; trocar)* |
| `Jwt__Issuer` | Emissor aceito. Precisa bater com o Users. | `FCG` |
| `Jwt__Audience` | Audiência aceita. Precisa bater com o Users. | `FCG` |
| `RabbitMq__Host` | Host do broker: `localhost` (dev), `rabbitmq` (compose/K8s), `host.docker.internal` (container isolado). | `localhost` |
| `RabbitMq__Username` | Usuário do RabbitMQ. | `guest` |
| `RabbitMq__Password` | Senha do RabbitMQ. | `guest` |
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita o Swagger. | `Production` (no container) |
| `ASPNETCORE_URLS` | Porta de escuta dentro do container. | `http://+:8080` |

### Portas

| Contexto | Endereço |
|----------|----------|
| `dotnet run` (Development) | http://localhost:5102 |
| Docker / compose | host `5102` → container `8080` |
| Service do Kubernetes | `catalog-api:80` → container `8080` |

---

## Kubernetes

Manifestos em [`k8s/`](k8s): `deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.yaml`.

```powershell
kubectl apply -f k8s/
kubectl get pods -l app=catalog-api
kubectl port-forward svc/catalog-api 5102:80
```

O Deployment usa `image: fcg/catalog-api:latest` com `imagePullPolicy: IfNotPresent` — **builde a imagem localmente antes** (seção Docker) para o cluster local encontrá-la.

Pré-requisito: um RabbitMQ acessível pelo Service `rabbitmq` no cluster (o manifesto vive no repositório `fcg-platform`).

> ⚠️ **Antes de produção:** troque `jwt-key` em `k8s/secret.yaml` — e aplique **o mesmo valor** no Secret do Users API.

---

## Endpoints

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/games` | Público | Lista os jogos do catálogo. |
| `POST` | `/api/games` | Administrador | Cadastra um jogo. |
| `PUT` | `/api/games/{id}` | Administrador | Atualiza um jogo. |
| `DELETE` | `/api/games/{id}` | Administrador | Inativa um jogo. |
| `POST` | `/api/games/{id}/reactivate` | Administrador | Reativa um jogo inativado. |
| `GET` | `/api/users/me/library` | Autenticado | Biblioteca do usuário. |
| `POST` | `/api/users/me/library/games/{gameId}` | Autenticado | **Inicia a compra**: publica `OrderPlacedEvent` e retorna `202 Accepted`. O jogo só entra na biblioteca depois que o Payments aprovar. |

---

## Eventos

| Evento | Direção | Efeito |
|--------|---------|--------|
| `OrderPlacedEvent` | **Publica** | Dispara o processamento do pagamento no Payments API. |
| `PaymentProcessedEvent` | **Consome** | Se aprovado, adiciona o jogo à biblioteca do usuário. Se recusado, a biblioteca não muda. |
