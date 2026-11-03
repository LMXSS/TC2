# FCG — Users API

Microsserviço de **cadastro e autenticação** da plataforma FIAP Cloud Games (FCG).

Responsabilidades:

- Registro de usuários (com validação de senha forte) e login.
- Emissão de **token JWT** consumido pelos demais microsserviços.
- Publicação do evento **`UserCreatedEvent`** no RabbitMQ após cada cadastro — consumido pelo `Notifications.Api`, que dispara o e-mail de boas-vindas.

Este repositório é **autocontido**: builda, containeriza e faz deploy sem depender de nenhum outro repositório da plataforma.

---

## Estrutura

```
fcg-users-api/
├── k8s/                       Manifestos Kubernetes (Deployment, Service, ConfigMap, Secret)
├── Users.Api/                 Código da API + Dockerfile
├── FCG.Messaging.Contracts/   Contratos de evento compartilhados entre os microsserviços
├── Users.Api.sln
└── README.md
```

> `FCG.Messaging.Contracts` é a **cópia** dos contratos de mensagem da plataforma. Os quatro microsserviços carregam a mesma cópia: alterar um evento exige replicar a mudança nos quatro repositórios.

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **RabbitMQ** acessível — obrigatório: a API publica evento a cada cadastro.

Subir um RabbitMQ local:

```powershell
docker run -d --name fcg-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management-alpine
```

Management UI: http://localhost:15672 (`guest` / `guest`)

---

## Executar

### Local (.NET SDK)

Da **raiz deste repositório**:

```powershell
dotnet run --project Users.Api
```

Swagger (Development): http://localhost:5101/swagger

As migrations são aplicadas automaticamente na subida. Em `Development`, um administrador de demonstração é semeado: `admin@fcg.local` / `Admin@123` (ver `Users.Api/appsettings.Development.json`).

### Docker

Da **raiz deste repositório** (o contexto do build é a raiz, pois o Dockerfile copia a API **e** os contratos):

```powershell
docker build -f Users.Api/Dockerfile -t fcg/users-api:latest .
docker run -p 5101:8080 -e RabbitMq__Host=host.docker.internal fcg/users-api:latest
```

> No Linux, troque `host.docker.internal` pelo IP do host para alcançar o RabbitMQ.

A imagem `fcg/users-api:latest` é a que o repositório de orquestração (`fcg-platform`) espera encontrar no `docker-compose`, e a mesma referenciada pelo `k8s/deployment.yaml`.

---

## Variáveis de ambiente

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `ConnectionStrings__DefaultConnection` | Banco SQLite. No container, aponte para o volume: `Data Source=/app/data/users.db`. | `Data Source=users.db` |
| `Jwt__Key` | Chave simétrica de assinatura, **mínimo 32 caracteres**. ⚠️ **Deve ser idêntica à do Catalog API** — senão o token emitido aqui é rejeitado lá. | *(valor de dev; trocar)* |
| `Jwt__Issuer` | Emissor do token. Precisa bater com o Catalog. | `FCG` |
| `Jwt__Audience` | Audiência do token. Precisa bater com o Catalog. | `FCG` |
| `Jwt__ExpiresMinutes` | Validade do token, em minutos. | `120` |
| `RabbitMq__Host` | Host do broker: `localhost` (dev), `rabbitmq` (compose/K8s), `host.docker.internal` (container isolado). | `localhost` |
| `RabbitMq__Username` | Usuário do RabbitMQ. | `guest` |
| `RabbitMq__Password` | Senha do RabbitMQ. | `guest` |
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita Swagger e o seed do admin. | `Production` (no container) |
| `ASPNETCORE_URLS` | Porta de escuta dentro do container. | `http://+:8080` |
| `Seed__AdminEmail` | *(Development)* E-mail do admin semeado. | `admin@fcg.local` |
| `Seed__AdminPassword` | *(Development)* Senha do admin semeado. | `Admin@123` |
| `Seed__AdminName` | *(Development)* Nome do admin semeado. | `Administrador` |

### Portas

| Contexto | Endereço |
|----------|----------|
| `dotnet run` (Development) | http://localhost:5101 |
| Docker / compose | host `5101` → container `8080` |
| Service do Kubernetes | `users-api:80` → container `8080` |

---

## Kubernetes

Manifestos em [`k8s/`](k8s): `deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.yaml`.

```powershell
kubectl apply -f k8s/
kubectl get pods -l app=users-api
kubectl port-forward svc/users-api 5101:80
```

O Deployment usa `image: fcg/users-api:latest` com `imagePullPolicy: IfNotPresent` — **builde a imagem localmente antes** (seção Docker) para o cluster local encontrá-la.

Pré-requisito: um RabbitMQ acessível pelo Service `rabbitmq` no cluster (o manifesto vive no repositório `fcg-platform`).

> ⚠️ **Antes de produção:** troque `jwt-key` em `k8s/secret.yaml` — o valor versionado é de desenvolvimento e é o mesmo do Catalog API.

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/api/auth/register` | Cadastra usuário e publica `UserCreatedEvent`. |
| `POST` | `/api/auth/login` | Autentica e devolve o token JWT. |
