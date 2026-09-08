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

    public DbSet<ShortStayDetail> ShortStayDetails => Set<ShortStayDetail>();

    public DbSet<InternationalDetail> InternationalDetails => Set<InternationalDetail>();

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
            entity.Property(p => p.IsShortStay).IsRequired();

            entity.HasMany(p => p.ChangeHistories)
                .WithOne(ch => ch.Pupil)
                .HasForeignKey(ch => ch.PupilId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShortStayDetail>(entity =>
        {
            entity.ToTable("ShortStayDetails");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.LengthOfStay).HasConversion<string>().IsRequired();

            // 0..1 per Pupil (AD-9): unique FK makes the one-to-one explicit.
            entity.HasIndex(s => s.PupilId).IsUnique();
            entity.HasOne(s => s.Pupil)
                .WithOne(p => p.ShortStayDetail)
                .HasForeignKey<ShortStayDetail>(s => s.PupilId)
                .OnDelete(DeleteBehavior.Cascade);

            // Nullable reference to the pupil's InternationalDetail row —
            // never a duplicate copy of agent/deposit/nationality (AD-9).
            entity.HasOne(s => s.InternationalDetail)
                .WithMany()
                .HasForeignKey(s => s.InternationalDetailId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InternationalDetail>(entity =>
        {
            entity.ToTable("InternationalDetails");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.AgentName).HasMaxLength(200);
            entity.Property(i => i.DepositDetail).HasMaxLength(200);
            entity.Property(i => i.Nationality).HasMaxLength(200);

            // 0..1 per Pupil (AD-9): unique FK makes the one-to-one explicit.
            entity.HasIndex(i => i.PupilId).IsUnique();
            entity.HasOne(i => i.Pupil)
                .WithOne(p => p.InternationalDetail)
                .HasForeignKey<InternationalDetail>(i => i.PupilId)
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
