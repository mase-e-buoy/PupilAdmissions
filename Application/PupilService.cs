using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// The single write path for pupil records (AD-3). Every create/edit/
/// status-change/archive operation for a <see cref="Pupil"/> must go
/// through this class — no other code writes to <c>Pupil</c> or
/// <c>ChangeHistory</c>. Story 2.1 implements <see cref="CreatePupilAsync"/>;
/// Story 2.2 adds <see cref="UpdatePupilAsync"/>; Story 2.3 extends both with
/// short-stay/international detail handling; later stories (2.4-2.6) add
/// discount/archive methods to this same service.
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
                IsShortStay = request.IsShortStay,
            };

            _db.Pupils.Add(pupil);

            var changedAtUtc = DateTime.UtcNow;
            var histories = new List<ChangeHistory>
            {
                NewHistory(pupil, nameof(Pupil.Name), null, pupil.Name, changedAtUtc),
                NewHistory(pupil, nameof(Pupil.YearGroup), null, pupil.YearGroup.ToDisplayName(), changedAtUtc),
                NewHistory(pupil, nameof(Pupil.BoardingType), null, pupil.BoardingType.ToDisplayName(), changedAtUtc),
                NewHistory(pupil, nameof(Pupil.Status), null, pupil.Status.ToDisplayName(), changedAtUtc),
            };

            // IsShortStay itself is never a ChangeHistory field (it is a
            // gate, not stored data) -- only the detail fields it reveals
            // are tracked, and only when populated (FR4, AD-9). Reuses the
            // same creation/diff logic UpdatePupilAsync uses -- against a
            // brand-new pupil with no existing ShortStayDetail/
            // InternationalDetail, every populated field diffs against
            // null and so is written as a fresh row.
            if (pupil.IsShortStay)
            {
                ApplyShortStayDetails(pupil, request.LengthOfStay!, request.AgentName, request.DepositDetail, request.Nationality, histories, changedAtUtc);
            }

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
    /// <see cref="UpdatePupilAsync"/> can mutate the loaded entity (and its
    /// 0..1 <see cref="ShortStayDetail"/>/<see cref="InternationalDetail"/>,
    /// AD-9) in place before <c>SaveChangesAsync</c>. Returns null when no
    /// pupil with the given id exists, so the caller (the Edit page) can
    /// return 404.
    /// </summary>
    public async Task<Pupil?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _db.Pupils
            .Include(p => p.ShortStayDetail)
            .Include(p => p.InternationalDetail)
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

            // IsShortStay itself is never a ChangeHistory field -- only the
            // detail fields it gates are tracked (FR4, AD-9). Assigning it
            // unconditionally is a no-op to the change tracker when the
            // value hasn't actually changed.
            pupil.IsShortStay = request.IsShortStay;

            if (request.IsShortStay)
            {
                ApplyShortStayDetails(pupil, request.LengthOfStay!, request.AgentName, request.DepositDetail, request.Nationality, histories, changedAtUtc);
            }
            else
            {
                ClearShortStayDetails(pupil, histories, changedAtUtc);
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

    /// <summary>
    /// Applies the four short-stay/international fields to <paramref name="pupil"/>
    /// (FR4, FR6, AD-9): creates <see cref="ShortStayDetail"/> if it does
    /// not yet exist, diffs <see cref="ShortStayDetail.LengthOfStay"/>
    /// against the stored value, and independently diffs/creates/clears the
    /// shared <see cref="InternationalDetail"/> row for agent name, deposit
    /// detail, and nationality -- each addable, editable, or clearable
    /// without affecting the others. Shared by both
    /// <see cref="CreatePupilAsync"/> (against a brand-new pupil with no
    /// existing detail rows) and <see cref="UpdatePupilAsync"/> (against a
    /// loaded pupil that may already have them) so the two flows cannot
    /// drift apart.
    /// </summary>
    private void ApplyShortStayDetails(Pupil pupil, string lengthOfStay, string? agentName, string? depositDetail, string? nationality, List<ChangeHistory> histories, DateTime changedAtUtc)
    {
        var shortStayDetail = pupil.ShortStayDetail;
        var previousLengthOfStay = shortStayDetail?.LengthOfStay;
        var newLengthOfStay = lengthOfStay.Trim();

        if (shortStayDetail is null)
        {
            shortStayDetail = new ShortStayDetail { Pupil = pupil, LengthOfStay = newLengthOfStay };
            _db.ShortStayDetails.Add(shortStayDetail);
            pupil.ShortStayDetail = shortStayDetail;
        }

        if (previousLengthOfStay != newLengthOfStay)
        {
            histories.Add(NewHistory(pupil, nameof(ShortStayDetail.LengthOfStay), previousLengthOfStay, newLengthOfStay, changedAtUtc));
            shortStayDetail.LengthOfStay = newLengthOfStay;
        }

        var normalizedAgentName = NormalizeOptional(agentName);
        var normalizedDepositDetail = NormalizeOptional(depositDetail);
        var normalizedNationality = NormalizeOptional(nationality);
        var anyPopulated = normalizedAgentName is not null || normalizedDepositDetail is not null || normalizedNationality is not null;

        var internationalDetail = pupil.InternationalDetail;
        if (internationalDetail is null && anyPopulated)
        {
            internationalDetail = new InternationalDetail { Pupil = pupil };
            _db.InternationalDetails.Add(internationalDetail);
            pupil.InternationalDetail = internationalDetail;
        }

        if (internationalDetail is not null)
        {
            // Keep ShortStayDetail's reference to the same InternationalDetail
            // row in sync (AD-9) rather than a duplicate copy of any field.
            shortStayDetail.InternationalDetail = internationalDetail;

            if (internationalDetail.AgentName != normalizedAgentName)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.AgentName), internationalDetail.AgentName, normalizedAgentName, changedAtUtc));
                internationalDetail.AgentName = normalizedAgentName;
            }

            if (internationalDetail.DepositDetail != normalizedDepositDetail)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.DepositDetail), internationalDetail.DepositDetail, normalizedDepositDetail, changedAtUtc));
                internationalDetail.DepositDetail = normalizedDepositDetail;
            }

            if (internationalDetail.Nationality != normalizedNationality)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.Nationality), internationalDetail.Nationality, normalizedNationality, changedAtUtc));
                internationalDetail.Nationality = normalizedNationality;
            }

            if (!anyPopulated)
            {
                // All three fields cleared -- no stale empty InternationalDetail
                // shell row survives (mirrors the un-flag boundary's intent).
                shortStayDetail.InternationalDetail = null;
                pupil.InternationalDetail = null;
                _db.InternationalDetails.Remove(internationalDetail);
            }
        }
    }

    /// <summary>
    /// Un-flagging short-stay (FR4 boundary): deletes the
    /// <see cref="ShortStayDetail"/>/<see cref="InternationalDetail"/> rows
    /// and appends one <see cref="ChangeHistory"/> row (new value null) per
    /// field that was previously populated -- no stale detail data survives
    /// under a non-short-stay pupil.
    /// </summary>
    private void ClearShortStayDetails(Pupil pupil, List<ChangeHistory> histories, DateTime changedAtUtc)
    {
        var shortStayDetail = pupil.ShortStayDetail;
        var internationalDetail = pupil.InternationalDetail;

        if (shortStayDetail is not null)
        {
            histories.Add(NewHistory(pupil, nameof(ShortStayDetail.LengthOfStay), shortStayDetail.LengthOfStay, null, changedAtUtc));
            shortStayDetail.InternationalDetail = null;
            pupil.ShortStayDetail = null;
            _db.ShortStayDetails.Remove(shortStayDetail);
        }

        if (internationalDetail is not null)
        {
            if (internationalDetail.AgentName is not null)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.AgentName), internationalDetail.AgentName, null, changedAtUtc));
            }

            if (internationalDetail.DepositDetail is not null)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.DepositDetail), internationalDetail.DepositDetail, null, changedAtUtc));
            }

            if (internationalDetail.Nationality is not null)
            {
                histories.Add(NewHistory(pupil, nameof(InternationalDetail.Nationality), internationalDetail.Nationality, null, changedAtUtc));
            }

            pupil.InternationalDetail = null;
            _db.InternationalDetails.Remove(internationalDetail);
        }
    }

    /// <summary>
    /// Blank/whitespace-only submitted values are treated as unset, not as
    /// an empty string (FR6); populated values are trimmed so leading/
    /// trailing whitespace is never stored or compared against.
    /// </summary>
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ChangeHistory NewHistory(Pupil pupil, string fieldName, string? previousValue, string? newValue, DateTime changedAtUtc) => new()
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
