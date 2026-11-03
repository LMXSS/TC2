using FCG.Messaging.Contracts;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Api.Domain;
using Payments.Api.Infrastructure;

namespace Payments.Api.Consumers;

public class OrderPlacedConsumer : IConsumer<OrderPlacedEvent>
{
    private readonly PaymentsDbContext _db;
    private readonly ILogger<OrderPlacedConsumer> _logger;
    private readonly PaymentSimulationOptions _options;

    public OrderPlacedConsumer(
        PaymentsDbContext db,
        ILogger<OrderPlacedConsumer> logger,
        IOptions<PaymentSimulationOptions> options)
    {
        _db = db;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Consume(ConsumeContext<OrderPlacedEvent> context)
    {
        var m = context.Message;

        if (await _db.ProcessedOrders.AnyAsync(o => o.OrderId == m.OrderId, context.CancellationToken))
            return;

        var status = PaymentSimulation.Resolve(_options, m.Price);

        _logger.LogInformation(
            "Pagamento simulado OrderId={OrderId} UserId={UserId} GameId={GameId} Price={Price} Mode={Mode} => {Status}",
            m.OrderId, m.UserId, m.GameId, m.Price, _options.SimulationMode, status);

        await context.Publish(
            new PaymentProcessedEvent(m.OrderId, m.UserId, m.GameId, status, m.UserEmail),
            context.CancellationToken);

        _db.ProcessedOrders.Add(new ProcessedOrder
        {
            OrderId = m.OrderId,
            UserId = m.UserId,
            GameId = m.GameId,
            Price = m.Price,
            Status = status,
            ProcessedAtUtc = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Idempotencia: reentrega do mesmo OrderId.
        }
    }

    // Só a violacao de chave (mesmo OrderId) e idempotencia. Outras falhas de escrita
    // -- "database is locked" do SQLite sob concorrencia, por exemplo -- precisam subir
    // para o MassTransit reentregar; engolidas, o pedido ficaria fora do ProcessedOrders
    // e seria reprocessado como novo numa eventual reentrega.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation };

    private const int SqliteConstraintViolation = 19;
}
