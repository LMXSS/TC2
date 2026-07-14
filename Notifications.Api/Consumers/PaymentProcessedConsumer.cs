using FCG.Messaging.Contracts;
using MassTransit;
using Notifications.Api.Infrastructure.Email;

namespace Notifications.Api.Consumers;

public class PaymentProcessedConsumer : IConsumer<PaymentProcessedEvent>
{
    private readonly IEmailSender _email;
    private readonly ILogger<PaymentProcessedConsumer> _logger;

    public PaymentProcessedConsumer(IEmailSender email, ILogger<PaymentProcessedConsumer> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var m = context.Message;
        var toEmail = string.IsNullOrWhiteSpace(m.UserEmail) ? $"user-{m.UserId:N}@fcg.local" : m.UserEmail;

        if (m.Status == PaymentStatus.Approved)
        {
            var html = $"""
                <h2>Compra confirmada!</h2>
                <p>Seu pagamento foi <strong>aprovado</strong>.</p>
                <ul>
                  <li>OrderId: {m.OrderId}</li>
                  <li>GameId: {m.GameId}</li>
                </ul>
                <p>O jogo ja esta disponivel na sua biblioteca.</p>
                """;
            await _email.SendAsync(toEmail, "Cliente FCG", "Confirmacao de compra — FCG", html, context.CancellationToken);
            _logger.LogInformation("Confirmacao de compra OrderId={OrderId} -> {Email}", m.OrderId, toEmail);
            return;
        }

        var rejectedHtml = $"""
            <h2>Pagamento nao aprovado</h2>
            <p>Infelizmente seu pagamento foi <strong>recusado</strong>.</p>
            <ul>
              <li>OrderId: {m.OrderId}</li>
              <li>GameId: {m.GameId}</li>
            </ul>
            <p>Voce pode tentar novamente com outro jogo.</p>
            """;
        await _email.SendAsync(toEmail, "Cliente FCG", "Pagamento recusado — FCG", rejectedHtml, context.CancellationToken);
        _logger.LogInformation("Notificacao de recusa OrderId={OrderId} -> {Email}", m.OrderId, toEmail);
    }
}
