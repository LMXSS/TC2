using Catalog.Api.Contracts;
using Catalog.Api.Domain.Entities;
using Catalog.Api.Infrastructure.Persistence;
using FCG.Messaging.Contracts;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api.Infrastructure.Services;

public class LibraryService
{
    private readonly CatalogDbContext _db;
    private readonly IPublishEndpoint _publish;

    public LibraryService(CatalogDbContext db, IPublishEndpoint publish)
    {
        _db = db;
        _publish = publish;
    }

    public async Task<IReadOnlyList<GameResponse>> GetMyLibraryAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var list = await _db.UsuarioJogos.AsNoTracking()
            .Where(uj => uj.UsuarioId == userId)
            .Join(_db.Jogos.IgnoreQueryFilters(), uj => uj.JogoId, j => j.Id, (uj, j) => j)
            .OrderBy(j => j.Titulo)
            .Select(j => new GameResponse(j.Id, j.Titulo, j.Genero, j.Preco))
            .ToListAsync(cancellationToken);
        return list;
    }

    public async Task<StartPurchaseResponse> StartPurchaseAsync(
        Guid userId,
        Guid gameId,
        string userEmail,
        CancellationToken cancellationToken = default)
    {
        var game = await _db.Jogos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, cancellationToken);
        if (game is null)
            throw new KeyNotFoundException("Jogo nao encontrado.");

        var already = await _db.UsuarioJogos.AnyAsync(
            uj => uj.UsuarioId == userId && uj.JogoId == gameId,
            cancellationToken);
        if (already)
            throw new InvalidOperationException("Jogo ja esta na sua biblioteca.");

        var orderId = Guid.NewGuid();
        await _publish.Publish(
            new OrderPlacedEvent(orderId, userId, gameId, game.Preco, userEmail),
            cancellationToken);

        return new StartPurchaseResponse(orderId, "Pedido enviado para processamento de pagamento.");
    }

    public async Task ConfirmPurchaseAsync(Guid userId, Guid gameId, CancellationToken cancellationToken = default)
    {
        var gameExists = await _db.Jogos.IgnoreQueryFilters().AnyAsync(g => g.Id == gameId && g.Ativo, cancellationToken);
        if (!gameExists)
            return;

        var already = await _db.UsuarioJogos.AnyAsync(
            uj => uj.UsuarioId == userId && uj.JogoId == gameId,
            cancellationToken);
        if (already)
            return;

        _db.UsuarioJogos.Add(new UsuarioJogo(userId, gameId));
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Idempotencia: outro consumidor ja inseriu o mesmo par (UsuarioId, JogoId).
        }
    }

    // Só a violacao da chave primaria (o par ja existe) e idempotencia. Qualquer outra
    // falha de escrita -- o "database is locked" do SQLite sob concorrencia, por exemplo --
    // precisa subir para o MassTransit reentregar a mensagem. Engolir tudo fazia a compra
    // sumir em silencio: a linha nao era gravada e o consumidor logava "Biblioteca
    // atualizada" logo em seguida, como se tivesse dado certo.
    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintViolation };

    private const int SqliteConstraintViolation = 19;
}
