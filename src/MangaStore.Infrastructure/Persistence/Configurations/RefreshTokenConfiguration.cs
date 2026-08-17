namespace MangaStore.Infrastructure.Persistence.Configurations;

using MangaStore.Domain.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>Configures the <see cref="RefreshToken"/> entity schema using the EF Core Fluent API.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>Length of a hex-encoded SHA-256 hash.</summary>
    private const int HashLength = 64;

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(HashLength)
            .IsFixedLength();

        builder.Property(t => t.ReplacedByTokenHash)
            .HasMaxLength(HashLength)
            .IsFixedLength();

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        // Long enough for an IPv6 address, including a mapped-IPv4 form.
        builder.Property(t => t.CreatedByIp)
            .HasMaxLength(45);

        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasQueryFilter(t => !t.IsDeleted);

        // Lookup on every refresh is by hash, and two tokens must never share one.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Supports revoking every session for a user.
        builder.HasIndex(t => t.UserId);

        builder.Ignore(t => t.DomainEvents);
    }
}
