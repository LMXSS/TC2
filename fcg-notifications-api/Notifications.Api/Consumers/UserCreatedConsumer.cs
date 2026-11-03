using FCG.Messaging.Contracts;
using MassTransit;
using Notifications.Api.Infrastructure.Email;

namespace Notifications.Api.Consumers;

public class UserCreatedConsumer : IConsumer<UserCreatedEvent>
{
    private readonly IEmailSender _email;
    private readonly ILogger<UserCreatedConsumer> _logger;

    public UserCreatedConsumer(IEmailSender email, ILogger<UserCreatedConsumer> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserCreatedEvent> context)
    {
        var m = context.Message;
        var html = $"""
            <h2>Bem-vindo(a) ao FIAP Cloud Games!</h2>
            <p>Ola, <strong>{m.Name}</strong>!</p>
            <p>Sua conta foi criada com sucesso.</p>
            <p><small>UserId: {m.UserId}</small></p>
            """;

        await _email.SendAsync(m.Email, m.Name, "Boas-vindas — FCG", html, context.CancellationToken);
        _logger.LogInformation("Boas-vindas enviada para {Email}", m.Email);
    }
}
