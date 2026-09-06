using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// The single write path for pupil records (AD-3). Every create/edit/
/// status-change/archive operation for a <see cref="Pupil"/> must go
/// through this class — no other code writes to <c>Pupil</c> or
/// <c>ChangeHistory</c>. Story 2.1 implements <see cref="CreatePupilAsync"/>;
/// Story 2.2 adds <see cref="UpdatePupilAsync"/>; later stories (2.3-2.6)
/// add archive/detail methods to this same service.
/// </summary>
public class PupilService
{
    // Process-wide write serialization (AD-3/AD-4): this app is the single
    // always-on writer for the SQLite file, so a static lock here
    // guarantees no two writes ever race each other in-process, and the
    // retry loop below is a defensive backstop against any remaining
    // transient contention (e.g. SQLITE_BUSY) rather than the primary
    // safeguard.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    private const int MaxAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IAppDbContext _db;

    public PupilService(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates a new pupil in one transaction, appending one
    /// <see cref="ChangeHistory"/> row per populated field (FR1, FR8, AD-3).
    /// Concurrent calls (from distinct requests) are serialized so they
    /// never surface a write conflict to the caller.
    /// </summary>
    public async Task<Pupil> CreatePupilAsync(CreatePupilRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);

        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Entities are added to the change tracker exactly once, outside
            // the retry loop: only the SaveChangesAsync call itself is
            // retried. If Add() were repeated on every attempt, a retry
            // after a transient failure would leave the still-tracked
            // entities from the failed attempt in place and add a second,
            // duplicate set alongside them.
            var pupil = new Pupil
            {
                Name = request.Name,
                YearGroup = request.YearGroup!.Value,
                BoardingType = request.BoardingType!.Value,
                Status = request.Status!.Value,
            };

            _db.Pupils.Add(pupil);

            var changedAtUtc = DateTime.UtcNow;
            var histories = new[]
            {
                NewHistory(pupil, nameof(Pupil.Name), null, pupil.Name, changedAtUtc),
                NewHistory(pupil, nameof(Pupil.YearGroup), null, pupil.YearGroup.ToDisplayName(), changedAtUtc),
                NewHistory(pupil, nameof(Pupil.BoardingType), null, pupil.BoardingType.ToDisplayName(), changedAtUtc),
                NewHistory(pupil, nameof(Pupil.Status), null, pupil.Status.ToDisplayName(), changedAtUtc),
            };
            _db.ChangeHistories.AddRange(histories);

            await ExecuteWithRetryAsync(
                () => _db.SaveChangesAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            return pupil;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    /// <summary>
    /// Loads a single pupil tracked by the change tracker (unlike
    /// <see cref="GetRosterAsync"/>'s <c>AsNoTracking()</c> read), so
    /// <see cref="UpdatePupilAsync"/> can mutate the loaded entity in place
    /// before <c>SaveChangesAsync</c>. Returns null when no pupil with the
    /// given id exists, so the caller (the Edit page) can return 404.
    /// </summary>
    public async Task<Pupil?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.Pupils
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Edits an existing pupil in one transaction (FR-2, AD-3): diffs each
    /// of the four editable fields against its current stored value and
    /// appends exactly one <see cref="ChangeHistory"/> row per field that
    /// actually changed — an unchanged field produces zero rows, and a
    /// no-op save (nothing changed) writes zero history rows and leaves the
    /// pupil row untouched. Status has no transition restriction — any
    /// status may move to any other status. Reuses the same write
    /// lock/retry machinery as <see cref="CreatePupilAsync"/> so there is
    /// still only one write path (AD-3) and concurrent edits to the same
    /// pupil never surface an error to the caller (AD-4).
    /// </summary>
    /// <returns>The updated pupil, or null if no pupil with <paramref name="id"/> exists.</returns>
    public async Task<Pupil?> UpdatePupilAsync(int id, UpdatePupilRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);

        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pupil = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (pupil is null)
            {
                return null;
            }

            var changedAtUtc = DateTime.UtcNow;
            var histories = new List<ChangeHistory>();

            if (pupil.Name != request.Name)
            {
                histories.Add(NewHistory(pupil, nameof(Pupil.Name), pupil.Name, request.Name, changedAtUtc));
                pupil.Name = request.Name;
            }

            if (pupil.YearGroup != request.YearGroup!.Value)
            {
                histories.Add(NewHistory(pupil, nameof(Pupil.YearGroup), pupil.YearGroup.ToDisplayName(), request.YearGroup.Value.ToDisplayName(), changedAtUtc));
                pupil.YearGroup = request.YearGroup.Value;
            }

            if (pupil.BoardingType != request.BoardingType!.Value)
            {
                histories.Add(NewHistory(pupil, nameof(Pupil.BoardingType), pupil.BoardingType.ToDisplayName(), request.BoardingType.Value.ToDisplayName(), changedAtUtc));
                pupil.BoardingType = request.BoardingType.Value;
            }

            if (pupil.Status != request.Status!.Value)
            {
                histories.Add(NewHistory(pupil, nameof(Pupil.Status), pupil.Status.ToDisplayName(), request.Status.Value.ToDisplayName(), changedAtUtc));
                pupil.Status = request.Status.Value;
            }

            if (histories.Count > 0)
            {
                _db.ChangeHistories.AddRange(histories);

                await ExecuteWithRetryAsync(
                    () => _db.SaveChangesAsync(cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            return pupil;
        }
        finally
        {
            WriteLock.Release();
        }
    }

    /// <summary>
    /// Reads the current (non-archived-concept-free in this story) roster,
    /// optionally filtered to a single year group, so a newly created pupil
    /// is immediately observable (AC). Read-only — does not participate in
    /// AD-3's write path.
    /// </summary>
    public async Task<IReadOnlyList<Pupil>> GetRosterAsync(YearGroup? yearGroup = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Pupils.AsNoTracking().AsQueryable();
        if (yearGroup is not null)
        {
            query = query.Where(p => p.YearGroup == yearGroup.Value);
        }

        return await query
            .OrderBy(p => p.YearGroup)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static ChangeHistory NewHistory(Pupil pupil, string fieldName, string? previousValue, string newValue, DateTime changedAtUtc) => new()
    {
        Pupil = pupil,
        FieldName = fieldName,
        PreviousValue = previousValue,
        NewValue = newValue,
        ActorId = ApplicationUser.SystemActorId,
        ChangedAtUtc = changedAtUtc,
    };

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Defensive backstop for transient single-writer contention
                // (e.g. SQLITE_BUSY) — never surfaced to the caller.
                await Task.Delay(RetryDelay * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
