using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Compass.Api.Data;

/// <summary>
/// The one database (ADR 0008): Identity's tables, the app's tables and the Data Protection key
/// ring share it. Column, index and constraint names are snake_cased by EFCore.NamingConventions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Build-time decision — Identity table names stay mixed-case.</b> <c>IdentityDbContext</c> pins
/// its seven tables with explicit <c>ToTable</c> calls ("AspNetUsers", "AspNetRoles", …), which the
/// naming convention does not touch. We accept that rather than re-mapping them: every ADR and
/// glossary entry already refers to <c>AspNetUsers</c>, and re-mapping would buy nothing but a
/// second name for the same table. The <see cref="Projection"/> foreign key therefore targets
/// <c>"AspNetUsers".id</c>.
/// </para>
/// <para>
/// The <c>data_protection_keys</c> table (from <see cref="IDataProtectionKeyContext"/>) is
/// <b>permanent</b>: it holds the key ring that encrypts the session cookie (ADR 0007).
/// </para>
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Projection> Projections => Set<Projection>();

    public DbSet<Scenario> Scenarios => Set<Scenario>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Projection>(projection =>
        {
            projection.HasKey(p => p.Id);
            projection.Property(p => p.Ticker).IsRequired().HasMaxLength(Projection.MaxTickerLength);
            projection.Property(p => p.CompanyName).IsRequired().HasMaxLength(Projection.MaxCompanyNameLength);

            // One projection per user per company (ADR 0004, amended). The ticker is stored
            // upper-cased, so this is the index on user plus upper-cased ticker.
            projection.HasIndex(p => new { p.UserId, p.Ticker }).IsUnique();

            // Wired explicitly because Projection deliberately has no navigation property to the user.
            // ON DELETE CASCADE: deleting a user deletes their projections in the database itself.
            projection.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            projection.HasMany(p => p.Scenarios)
                .WithOne()
                .HasForeignKey(s => s.ProjectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Scenario>(scenario =>
        {
            scenario.HasKey(s => s.Id);
            scenario.Property(s => s.Name).IsRequired().HasMaxLength(8);
            scenario.Property(s => s.ExitMultipleKind).IsRequired().HasMaxLength(2);
            // Bear, Base and Bull once each is what holds a projection to three.
            scenario.HasIndex(s => new { s.ProjectionId, s.Name }).IsUnique();
        });
    }
}
