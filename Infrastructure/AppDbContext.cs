using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Infrastructure;

/// <summary>
/// EF Core implementation of the persistence port (AD-8). Enums are stored
/// as strings (never a raw int/parallel representation) so the SQLite data
/// stays human-readable and there is exactly one canonical representation
/// of status/boarding type/year group (AD-1).
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Pupil> Pupils => Set<Pupil>();

    public DbSet<ChangeHistory> ChangeHistories => Set<ChangeHistory>();

    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Pupil>(entity =>
        {
            entity.ToTable("Pupils");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(200);
            entity.Property(p => p.YearGroup).HasConversion<string>().IsRequired();
            entity.Property(p => p.BoardingType).HasConversion<string>().IsRequired();
            entity.Property(p => p.Status).HasConversion<string>().IsRequired();

            entity.HasMany(p => p.ChangeHistories)
                .WithOne(ch => ch.Pupil)
                .HasForeignKey(ch => ch.PupilId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChangeHistory>(entity =>
        {
            entity.ToTable("ChangeHistories");
            entity.HasKey(ch => ch.Id);
            entity.Property(ch => ch.FieldName).IsRequired().HasMaxLength(100);
            entity.Property(ch => ch.ChangedAtUtc)
                .IsRequired()
                .HasConversion(
                    toDb => toDb,
                    fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

            entity.HasOne(ch => ch.Actor)
                .WithMany()
                .HasForeignKey(ch => ch.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("ApplicationUsers");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Name).IsRequired().HasMaxLength(200);

            // Seeded hardcoded "system" actor (constant id) until Epic 1
            // ships real login — every ChangeHistory row still has a
            // non-null actor (FR8).
            entity.HasData(new ApplicationUser
            {
                Id = ApplicationUser.SystemActorId,
                Name = "System",
            });
        });
    }
}
