using static BCrypt.Net.BCrypt;
using FCG.Messaging.Contracts;
using MassTransit;
using Users.Api.Contracts;
using Users.Api.Domain.Constants;
using Users.Api.Domain.Entities;
using Users.Api.Domain.Services;
using Users.Api.Infrastructure.Persistence;
using Users.Api.Infrastructure.Security;

namespace Users.Api.Infrastructure.Services;

public class AuthService
{
    private readonly UsuarioRepository _usuarios;
    private readonly JwtTokenGenerator _jwt;
    private readonly IPublishEndpoint _publish;

    public AuthService(UsuarioRepository usuarios, JwtTokenGenerator jwt, IPublishEndpoint publish)
    {
        _usuarios = usuarios;
        _jwt = jwt;
        _publish = publish;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Nome e obrigatorio.");

        var email = request.Email.Trim().ToLowerInvariant();

        if (!CredentialValidation.IsValidEmail(email))
            throw new InvalidOperationException("E-mail invalido.");

        if (!CredentialValidation.IsStrongPassword(request.Password, out var pwdError))
            throw new InvalidOperationException(pwdError!);

        if (await _usuarios.EmailExistsAsync(email, cancellationToken))
            throw new InvalidOperationException("E-mail ja cadastrado.");

        var hash = HashPassword(request.Password);
        var usuario = new Usuario(request.Name.Trim(), email, hash, Roles.Usuario);

        await _usuarios.AddAsync(usuario, cancellationToken);

        await _publish.Publish(new UserCreatedEvent(usuario.Id, usuario.Email, usuario.Nome), cancellationToken);

        var now = DateTime.UtcNow;
        var token = _jwt.CreateToken(usuario, now);
        return new AuthResponse(token, _jwt.GetExpiryUtc(now), usuario.Id, usuario.Nome, usuario.Email, usuario.Perfil);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var usuario = await _usuarios.GetByEmailAsync(email, cancellationToken);
        if (usuario is null)
            throw new UnauthorizedAccessException("Credenciais invalidas.");

        if (!Verify(request.Password, usuario.SenhaHash))
            throw new UnauthorizedAccessException("Credenciais invalidas.");

        var now = DateTime.UtcNow;
        var token = _jwt.CreateToken(usuario, now);
        return new AuthResponse(token, _jwt.GetExpiryUtc(now), usuario.Id, usuario.Nome, usuario.Email, usuario.Perfil);
    }
}
