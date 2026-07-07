using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intervals.Api.Data;

public sealed class IntervalsDbContext : DbContext
{
    public IntervalsDbContext(DbContextOptions<IntervalsDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<PasswordCredential> PasswordCredentials => Set<PasswordCredential>();
    public DbSet<AuthEvent> AuthEvents => Set<AuthEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.EmailNormalized).HasMaxLength(320);
            entity.Property(e => e.AvatarUrl).HasMaxLength(2048);
            entity.HasIndex(e => e.EmailNormalized);
        });

        modelBuilder.Entity<ExternalLogin>(entity =>
        {
            entity.ToTable("ExternalLogins");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ProviderUserId).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Email).HasMaxLength(320);
            entity.Property(e => e.DisplayName).HasMaxLength(256);
            entity.Property(e => e.AvatarUrl).HasMaxLength(2048);
            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            entity
                .HasOne(e => e.User)
                .WithMany(u => u.ExternalLogins)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordCredential>(entity =>
        {
            entity.ToTable("PasswordCredentials");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(320);
            entity.Property(e => e.EmailNormalized).IsRequired().HasMaxLength(320);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.HasIndex(e => e.EmailNormalized).IsUnique();
            entity
                .HasOne(e => e.User)
                .WithOne(u => u.PasswordCredential)
                .HasForeignKey<PasswordCredential>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuthEvent>(entity =>
        {
            entity.ToTable("AuthEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).HasMaxLength(64);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.FailureCode).HasMaxLength(64);
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.HasIndex(e => e.OccurredUtc);
        });
    }
}
