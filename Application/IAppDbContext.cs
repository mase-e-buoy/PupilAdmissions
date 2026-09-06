using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// Persistence port (AD-8): the only surface <c>PupilService</c> uses to
/// reach storage. <c>Infrastructure</c>'s EF Core <c>AppDbContext</c>
/// implements this; Web/Application never reference Infrastructure or EF
/// Core provider types directly.
/// </summary>
public interface IAppDbContext
{
    DbSet<Pupil> Pupils { get; }

    DbSet<ChangeHistory> ChangeHistories { get; }

    DbSet<ApplicationUser> ApplicationUsers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
