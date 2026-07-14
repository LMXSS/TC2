using System.Text;
using Catalog.Api.Consumers;
using Catalog.Api.Infrastructure.Persistence;
using Catalog.Api.Infrastructure.Security;
using Catalog.Api.Infrastructure.Services;
using Catalog.Api.Middleware;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "FCG Catalog API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT emitido pelo Users API (Authorization: Bearer {token})",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection ausente.");
connectionString = ResolveSqlitePath(connectionString, builder.Environment.ContentRootPath);
builder.Services.AddDbContext<CatalogDbContext>(o => o.UseSqlite(connectionString));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<GameService>();
builder.Services.AddScoped<LibraryService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt ausente.");
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key com pelo menos 32 caracteres.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

var rabbitHost = builder.Configuration["RabbitMq:Host"] ?? "localhost";
var rabbitUser = builder.Configuration["RabbitMq:Username"] ?? "guest";
var rabbitPass = builder.Configuration["RabbitMq:Password"] ?? "guest";

builder.Services.AddMassTransit(x =>
{
    // O prefixo do servico e obrigatorio: sem ele, o nome da fila sai do nome da
    // classe do consumidor, e Catalog e Notifications tem um PaymentProcessedConsumer
    // cada. Os dois cairiam na MESMA fila e virariam consumidores concorrentes --
    // o RabbitMQ entregaria cada PaymentProcessedEvent a apenas um deles, entao a
    // compra ora atualizaria a biblioteca, ora enviaria o e-mail, nunca os dois.
    // Com o prefixo, cada servico tem sua propria fila ligada ao mesmo exchange.
    x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("catalog", false));
    x.AddConsumer<PaymentProcessedConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitHost, "/", h =>
        {
            h.Username(rabbitUser);
            h.Password(rabbitPass);
        });
        // Sob concorrencia o SQLite recusa escritas com "database is locked". Essas
        // falhas sao transitorias: reentregar a mensagem resolve. Sem retry, elas
        // iriam direto para a fila de erro e a compra se perderia.
        cfg.UseMessageRetry(r => r.Interval(5, TimeSpan.FromMilliseconds(200)));
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseApiExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static string ResolveSqlitePath(string connectionString, string contentRoot)
{
    var b = new SqliteConnectionStringBuilder(connectionString);
    var ds = b.DataSource;
    if (string.IsNullOrEmpty(ds) || ds.Equals(":memory:", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(ds))
        return connectionString;
    b.DataSource = Path.GetFullPath(Path.Combine(contentRoot, ds));
    return b.ConnectionString;
}
