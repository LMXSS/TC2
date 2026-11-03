# FCG — Notifications API

Microsserviço de **notificações por e-mail** da plataforma FIAP Cloud Games (FCG).

Responsabilidades:

- Consome **`UserCreatedEvent`** (publicado pelo Users API) → envia o e-mail de **boas-vindas**.
- Consome **`PaymentProcessedEvent`** (publicado pelo Payments API) → envia a **confirmação de compra** ou o **aviso de recusa**.
- Entrega os e-mails via **SMTP**. Em desenvolvimento, o destino é o **Mailpit**, um servidor SMTP falso com caixa de entrada web.

Este serviço **não expõe endpoints de negócio** — todo o trabalho acontece nos consumidores MassTransit. Para ver que ele funcionou, olhe a caixa do Mailpit ou os logs do container.

Este repositório é **autocontido**: builda, containeriza e faz deploy sem depender de nenhum outro repositório da plataforma.

---

## Estrutura

```
fcg-notifications-api/
├── k8s/                       Manifestos Kubernetes (Deployment, Service, ConfigMap, Secret)
├── Notifications.Api/         Código da API + Dockerfile
├── FCG.Messaging.Contracts/   Contratos de evento compartilhados entre os microsserviços
├── Notifications.Api.sln
└── README.md
```

> `FCG.Messaging.Contracts` é a **cópia** dos contratos de mensagem da plataforma. Os quatro microsserviços carregam a mesma cópia: alterar um evento exige replicar a mudança nos quatro repositórios.

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **RabbitMQ** acessível.
- Um **servidor SMTP** acessível — em desenvolvimento, o Mailpit.
- Para exercitar os fluxos: **Users API** (cadastro → boas-vindas) e **Catalog + Payments** (compra → confirmação/recusa).

Subir RabbitMQ e Mailpit localmente:

```powershell
docker run -d --name fcg-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management-alpine
docker run -d --name fcg-mailpit -p 8025:8025 -p 1025:1025 axllent/mailpit:latest
```

Caixa de entrada do Mailpit: **http://localhost:8025** — é onde os e-mails aparecem.

---

## Executar

### Local (.NET SDK)

Da **raiz deste repositório**:

```powershell
dotnet run --project Notifications.Api
```

Health: http://localhost:5104/health
Swagger (Development): http://localhost:5104/swagger

### Docker

Da **raiz deste repositório** (o contexto do build é a raiz, pois o Dockerfile copia a API **e** os contratos):

```powershell
docker build -f Notifications.Api/Dockerfile -t fcg/notifications-api:latest .
docker run -p 5104:8080 `
  -e RabbitMq__Host=host.docker.internal `
  -e Smtp__Host=host.docker.internal `
  fcg/notifications-api:latest
```

> No Linux, troque `host.docker.internal` pelo IP do host para alcançar o RabbitMQ e o Mailpit.

A imagem `fcg/notifications-api:latest` é a que o repositório de orquestração (`fcg-platform`) espera encontrar no `docker-compose`, e a mesma referenciada pelo `k8s/deployment.yaml`.

### Conferir que funcionou

- **Boas-vindas:** registre um usuário no Users API → o e-mail aparece no Mailpit (http://localhost:8025).
- **Confirmação de compra:** conclua uma compra pelo Catalog API → chega o e-mail de confirmação (ou de recusa, se o pagamento for negado).

---

## Variáveis de ambiente

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `Smtp__Enabled` | Liga o envio real por SMTP. Com `false`, o e-mail é apenas logado. | `true` |
| `Smtp__Host` | Host do servidor SMTP: `localhost` (dev), `mailpit` (compose/K8s), `host.docker.internal` (container isolado). | `localhost` |
| `Smtp__Port` | Porta SMTP. | `1025` |
| `Smtp__FromAddress` | Remetente dos e-mails. | `noreply@fcg.local` |
| `Smtp__FromName` | Nome exibido do remetente. | `FIAP Cloud Games` |
| `RabbitMq__Host` | Host do broker: `localhost` (dev), `rabbitmq` (compose/K8s), `host.docker.internal` (container isolado). | `localhost` |
| `RabbitMq__Username` | Usuário do RabbitMQ. | `guest` |
| `RabbitMq__Password` | Senha do RabbitMQ. | `guest` |
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita o Swagger. | `Production` (no container) |
| `ASPNETCORE_URLS` | Porta de escuta dentro do container. | `http://+:8080` |

> Este serviço **não valida JWT** e **não tem banco de dados** — é acionado exclusivamente por evento. Nenhuma `Jwt__*` ou `ConnectionStrings__*` é necessária aqui.

### Portas

| Contexto | Endereço |
|----------|----------|
| `dotnet run` (Development) | http://localhost:5104 |
| Docker / compose | host `5104` → container `8080` |
| Service do Kubernetes | `notifications-api:80` → container `8080` |

---

## Kubernetes

Manifestos em [`k8s/`](k8s): `deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.yaml`.

```powershell
kubectl apply -f k8s/
kubectl get pods -l app=notifications-api
kubectl logs -l app=notifications-api -f
```

O Deployment usa `image: fcg/notifications-api:latest` com `imagePullPolicy: IfNotPresent` — **builde a imagem localmente antes** (seção Docker) para o cluster local encontrá-la.

Pré-requisitos no cluster:

- Um **RabbitMQ** acessível pelo Service `rabbitmq`.
- Um **Mailpit** acessível pelo Service `mailpit` — o ConfigMap aponta `Smtp__Host: mailpit`. Sem ele, os consumidores processam o evento mas o envio do e-mail falha.

Ambos os manifestos vivem no repositório **`fcg-platform`**.

---

## Eventos

| Evento | Direção | Efeito |
|--------|---------|--------|
| `UserCreatedEvent` | **Consome** | Envia o e-mail de boas-vindas ao novo usuário. |
| `PaymentProcessedEvent` | **Consome** | Envia a confirmação de compra (aprovado) ou o aviso de recusa (rejeitado). |
