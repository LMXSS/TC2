# Teste de pagamentos (aprovado / reprovado) e Mailpit

## Mailpit — caixa de e-mails

| Item | Valor |
|------|--------|
| **Web UI** | http://localhost:8025 |
| **SMTP** | `mailpit:1025` (Docker) ou `localhost:1025` (local) |

Todos os e-mails (boas-vindas, compra aprovada, compra recusada) aparecem na interface do Mailpit.

---

## Regras de pagamento simulado

Configuracao em `Payments__SimulationMode` (compose / appsettings):

| Modo | Comportamento |
|------|----------------|
| `Random` (padrao) | Aprova ~92% dos pedidos |
| `AlwaysApprove` | Sempre aprova |
| `AlwaysReject` | Sempre recusa |

**Preco fixo para demo (modo Random):**

| Preco do jogo | Resultado |
|---------------|-----------|
| **49.90** (ou qualquer valor != 99.99) | Tende a **Approved** |
| **99.99** | Sempre **Rejected** |

Consultar config atual: http://localhost:5103/swagger → `GET /api/payments/simulation`

Consultar status de um pedido: `GET /api/payments/orders/{orderId}`

---

## Cenario 1 — Pagamento APROVADO

1. **Register** (Users 5101): `teste@fcg.local` / `Senha@123`
2. Ver e-mail de boas-vindas no **Mailpit** (8025)
3. **Login admin** → criar jogo no Catalog (5102):

```json
{
  "titulo": "Jogo Aprovado",
  "genero": "RPG",
  "preco": 49.90
}
```

4. **Login usuario** → `POST /api/users/me/library/games/{gameId}`
5. Anotar `orderId` da resposta 202
6. **Payments** (5103): `GET /api/payments/orders/{orderId}` → `"status": "Approved"`
7. **Catalog**: `GET /api/users/me/library` → jogo na lista
8. **Mailpit** → e-mail "Confirmacao de compra"

---

## Cenario 2 — Pagamento REPROVADO

1. Admin cria jogo com preco **99.99**:

```json
{
  "titulo": "Jogo Recusado",
  "genero": "Horror",
  "preco": 99.99
}
```

2. Usuario compra esse jogo (`POST .../library/games/{gameId}`)
3. `GET /api/payments/orders/{orderId}` → `"status": "Rejected"`
4. `GET /api/users/me/library` → **nao** inclui o jogo
5. **Mailpit** → e-mail "Pagamento recusado"

---

## Forcar sempre aprovar ou recusar (sem mudar preco)

Edite `docker-compose.yml` em `payments-api`:

```yaml
Payments__SimulationMode: AlwaysApprove
# ou
Payments__SimulationMode: AlwaysReject
```

Recrie o container (e so variavel de ambiente — nao precisa rebuildar a imagem):

```powershell
docker compose up -d payments-api
```

---

## Subir stack com Mailpit

```powershell
.\build-images.ps1     # so na primeira vez (ou apos mudar codigo de alguma API)
docker compose up -d
```

URLs uteis:

| Ferramenta | URL |
|------------|-----|
| Mailpit | http://localhost:8025 |
| RabbitMQ | http://localhost:15672 (guest/guest) |
| Payments Swagger | http://localhost:5103/swagger |
