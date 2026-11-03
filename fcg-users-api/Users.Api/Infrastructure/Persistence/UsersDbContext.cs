using Microsoft.EntityFrameworkCore;
using Users.Api.Domain.Constants;
using Users.Api.Domain.Entities;

namespace Users.Api.Infrastructure.Persistence;

public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("Usuarios");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Nome).HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.SenhaHash).HasMaxLength(500);
            entity.Property(e => e.Perfil).HasMaxLength(50);
            entity.HasIndex(e => e.Email).IsUnique().HasFilter("\"Ativo\" = 1");
            entity.HasQueryFilter(e => e.Ativo);
        });
    }
}
