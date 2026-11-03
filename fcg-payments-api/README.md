# FCG — Payments API

Microsserviço de **processamento de pagamentos** da plataforma FIAP Cloud Games (FCG).

Responsabilidades:

- Consome **`OrderPlacedEvent`** publicado pelo Catalog API a cada compra iniciada.
- **Simula** a aprovação ou recusa do pagamento (não há gateway real).
- Publica **`PaymentProcessedEvent`** com o resultado — consumido pelo Catalog (libera o jogo na biblioteca) e pelo Notifications (envia o e-mail).
- Expõe endpoints de consulta para inspecionar a simulação e o resultado de um pedido.

Este repositório é **autocontido**: builda, containeriza e faz deploy sem depender de nenhum outro repositório da plataforma.

---

## Estrutura

```
fcg-payments-api/
├── k8s/                       Manifestos Kubernetes (Deployment, Service, ConfigMap, Secret)
├── Payments.Api/              Código da API + Dockerfile
├── FCG.Messaging.Contracts/   Contratos de evento compartilhados entre os microsserviços
├── Payments.Api.sln
└── README.md
```

> `FCG.Messaging.Contracts` é a **cópia** dos contratos de mensagem da plataforma. Os quatro microsserviços carregam a mesma cópia: alterar um evento exige replicar a mudança nos quatro repositórios.

---

## Simulação de pagamento

O resultado é decidido pelo bloco `Payments` da configuração:

| Chave | Efeito |
|-------|--------|
| `Payments__SimulationMode` | `Random` (padrão) — aprova conforme a `ApprovalRate`. |
| `Payments__ApprovalRate` | Probabilidade de aprovação, de `0` a `1`. Padrão `0.92`. |
| `Payments__RejectPrices` | Lista de preços **sempre recusados**, independente da taxa. Padrão: `99.99`. |

Para demonstrar os dois caminhos de forma previsível, use o preço do jogo:

| Preço do jogo | Resultado |
|---------------|-----------|
| `49.90` (ou qualquer outro) | Aprovado na maioria das vezes (`ApprovalRate` = 0.92). |
| **`99.99`** | **Sempre recusado** — está em `RejectPrices`. |

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **RabbitMQ** acessível.
- **Catalog API** no ar para publicar `OrderPlacedEvent` — sem ele, este serviço sobe mas não tem o que consumir.

Subir um RabbitMQ local:

```powershell
docker run -d --name fcg-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management-alpine
```

---

## Executar

### Local (.NET SDK)

Da **raiz deste repositório**:

```powershell
dotnet run --project Payments.Api
```

Health: http://localhost:5103/health
Swagger (Development): http://localhost:5103/swagger

As migrations são aplicadas automaticamente na subida.

### Docker

Da **raiz deste repositório** (o contexto do build é a raiz, pois o Dockerfile copia a API **e** os contratos):

```powershell
docker build -f Payments.Api/Dockerfile -t fcg/payments-api:latest .
docker run -p 5103:8080 -e RabbitMq__Host=host.docker.internal fcg/payments-api:latest
```

> No Linux, troque `host.docker.internal` pelo IP do host para alcançar o RabbitMQ.

A imagem `fcg/payments-api:latest` é a que o repositório de orquestração (`fcg-platform`) espera encontrar no `docker-compose`, e a mesma referenciada pelo `k8s/deployment.yaml`.

### Conferir que o consumo funcionou

Dispare uma compra no Catalog (`POST /api/users/me/library/games/{gameId}`) e acompanhe os logs deste serviço — deve aparecer `Pagamento simulado ... Approved` ou `Rejected`.

---

## Variáveis de ambiente

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `ConnectionStrings__DefaultConnection` | Banco SQLite dos pedidos processados. No container, aponte para o volume: `Data Source=/app/data/payments.db`. | `Data Source=payments.db` |
| `Payments__SimulationMode` | Estratégia de simulação. | `Random` |
| `Payments__ApprovalRate` | Probabilidade de aprovação (`0`–`1`). | `0.92` |
| `Payments__RejectPrices` | Preços sempre recusados (lista). | `[99.99]` |
| `RabbitMq__Host` | Host do broker: `localhost` (dev), `rabbitmq` (compose/K8s), `host.docker.internal` (container isolado). | `localhost` |
| `RabbitMq__Username` | Usuário do RabbitMQ. | `guest` |
| `RabbitMq__Password` | Senha do RabbitMQ. | `guest` |
| `ASPNETCORE_ENVIRONMENT` | `Development` habilita o Swagger. | `Production` (no container) |
| `ASPNETCORE_URLS` | Porta de escuta dentro do container. | `http://+:8080` |

> Este serviço **não valida JWT** — ele não expõe endpoints protegidos e é acionado por evento. Nenhuma `Jwt__*` é necessária aqui.

### Portas

| Contexto | Endereço |
|----------|----------|
| `dotnet run` (Development) | http://localhost:5103 |
| Docker / compose | host `5103` → container `8080` |
| Service do Kubernetes | `payments-api:80` → container `8080` |

---

## Kubernetes

Manifestos em [`k8s/`](k8s): `deployment.yaml`, `service.yaml`, `configmap.yaml`, `secret.yaml`.

```powershell
kubectl apply -f k8s/
kubectl get pods -l app=payments-api
kubectl port-forward svc/payments-api 5103:80
```

O Deployment usa `image: fcg/payments-api:latest` com `imagePullPolicy: IfNotPresent` — **builde a imagem localmente antes** (seção Docker) para o cluster local encontrá-la.

Pré-requisito: um RabbitMQ acessível pelo Service `rabbitmq` no cluster (o manifesto vive no repositório `fcg-platform`).

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| `GET` | `/health` | Health check. |
| `GET` | `/api/payments/simulation` | Configuração de simulação em vigor (modo, taxa, preços recusados). |
| `GET` | `/api/payments/orders/{orderId}` | Resultado do pagamento de um pedido já processado. |

---

## Eventos

| Evento | Direção | Efeito |
|--------|---------|--------|
| `OrderPlacedEvent` | **Consome** | Simula o pagamento e persiste o pedido processado. |
| `PaymentProcessedEvent` | **Publica** | Informa o resultado ao Catalog (libera a biblioteca) e ao Notifications (dispara o e-mail). |
