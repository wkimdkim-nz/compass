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
/// second name for the same table. The <see cref="Note"/> foreign key therefore targets
/// <c>"AspNetUsers".id</c>.
/// </para>
/// <para>
/// The <c>data_protection_keys</c> table (from <see cref="IDataProtectionKeyContext"/>) is
/// <b>permanent</b>. It is not part of the throwaway skeleton and must survive the deletion of
/// <see cref="Note"/>.
/// </para>
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Note>(note =>
        {
            note.HasKey(n => n.Id);
            note.Property(n => n.Text).IsRequired().HasMaxLength(Note.MaxTextLength);

            // Wired explicitly because Note deliberately has no navigation property.
            // ON DELETE CASCADE: deleting a user deletes their notes in the database itself.
            note.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
