using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;
using PupilAdmissions.Infrastructure;

namespace PupilAdmissions.Application.Tests;

/// <summary>
/// Covers the two AD-3 invariants this story must hold: one
/// <see cref="ChangeHistory"/> row per populated field on create, and safe
/// behavior under concurrent creates from two distinct callers.
/// </summary>
public class PupilServiceTests : IDisposable
{
    private readonly string _dbPath;

    public PupilServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pupiladmissions-tests-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by default, which keeps
        // the underlying file handle open after a DbContext is disposed —
        // release the pool before deleting the temp file plus any WAL/SHM
        // sidecar files SQLite may have created.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm", _dbPath + "-journal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup only; a leftover temp file does not fail the test.
            }
            catch (UnauthorizedAccessException)
            {
                // SQLite's Windows file-locking can still hold the handle briefly
                // even after ClearAllPools(); best-effort cleanup only.
            }
        }
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Builds schema by applying the real EF Core migration (rather than
    /// <see cref="DbContext.Database"/>'s <c>EnsureCreated()</c>, which never
    /// reads the migration file) so model/migration drift is caught here
    /// instead of only on the real app's first startup.
    /// </summary>
    private AppDbContext CreateMigratedContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        var context = new AppDbContext(options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task EnsureCreated_SeedsHardcodedSystemActor()
    {
        await using var db = CreateContext();

        var users = await db.ApplicationUsers.ToListAsync();

        Assert.Single(users);
        Assert.Equal(ApplicationUser.SystemActorId, users[0].Id);
    }

    [Fact]
    public async Task CreatePupilAsync_HappyPath_SavesPupilAndAppearsInRoster()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Ada Lovelace",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        Assert.True(pupil.Id > 0);

        var roster = await service.GetRosterAsync(YearGroup.Year7);
        Assert.Contains(roster, p => p.Id == pupil.Id && p.Name == "Ada Lovelace");
    }

    [Fact]
    public async Task CreatePupilAsync_WritesOneChangeHistoryRowPerPopulatedField()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Grace Hopper",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.PipelineOffered,
        });

        var histories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id)
            .OrderBy(ch => ch.FieldName)
            .ToListAsync();

        // One row per populated field (all four are required on create) —
        // never one composite row per save (AD-3).
        Assert.Equal(4, histories.Count);

        var byField = histories.ToDictionary(h => h.FieldName);
        Assert.Equal("Grace Hopper", byField[nameof(Pupil.Name)].NewValue);
        Assert.Equal("Year 9", byField[nameof(Pupil.YearGroup)].NewValue);
        Assert.Equal("Full Board", byField[nameof(Pupil.BoardingType)].NewValue);
        Assert.Equal("Pipeline (offered)", byField[nameof(Pupil.Status)].NewValue);

        foreach (var history in histories)
        {
            // Every row has a non-null actor and UTC timestamp (FR8).
            Assert.Equal(ApplicationUser.SystemActorId, history.ActorId);
            Assert.Equal(DateTimeKind.Utc, history.ChangedAtUtc.Kind);
            Assert.Null(history.PreviousValue);
        }
    }

    [Fact]
    public async Task CreatePupilAsync_MissingRequiredField_ThrowsAndDoesNotSaveHalfARecord()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "",
            YearGroup = YearGroup.Year10,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        }));

        Assert.Empty(await db.Pupils.ToListAsync());
        Assert.Empty(await db.ChangeHistories.ToListAsync());
    }

    [Fact]
    public async Task CreatePupilAsync_ConcurrentCreatesByTwoStaff_BothSavedAsSeparateRows()
    {
        // Two distinct DbContext/PupilService instances, as two concurrent
        // HTTP requests in the real app would get — PupilService's static
        // write lock still serializes them so neither write is lost, and
        // no SQLITE_BUSY/DbUpdateException is ever surfaced to the caller.
        await using var dbA = CreateContext();
        await using var dbB = CreateContext();
        var serviceA = new PupilService(dbA);
        var serviceB = new PupilService(dbB);

        var requestA = new CreatePupilRequest
        {
            Name = "Pupil A",
            YearGroup = YearGroup.Year5,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        };
        var requestB = new CreatePupilRequest
        {
            Name = "Pupil B",
            YearGroup = YearGroup.Year6,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.Joiner,
        };

        var results = await Task.WhenAll(
            serviceA.CreatePupilAsync(requestA),
            serviceB.CreatePupilAsync(requestB));

        Assert.NotEqual(results[0].Id, results[1].Id);

        await using var verifyDb = CreateContext();
        var names = await verifyDb.Pupils.Select(p => p.Name).ToListAsync();
        Assert.Contains("Pupil A", names);
        Assert.Contains("Pupil B", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public async Task Migrate_SeedsSystemActorAndSupportsCreateAndRosterRoundTrip()
    {
        await using var db = CreateMigratedContext();

        var users = await db.ApplicationUsers.ToListAsync();
        Assert.Single(users);
        Assert.Equal(ApplicationUser.SystemActorId, users[0].Id);

        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Katherine Johnson",
            YearGroup = YearGroup.Year8,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
        });

        var roster = await service.GetRosterAsync(YearGroup.Year8);
        Assert.Contains(roster, p => p.Id == pupil.Id && p.Name == "Katherine Johnson");
    }

    [Fact]
    public async Task UpdatePupilAsync_SingleFieldEdit_WritesExactlyOneCorrectChangeHistoryRow()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Rosalind Franklin",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = PupilStatus.LeaverNoticeGiven,
        });

        Assert.NotNull(updated);
        Assert.Equal(PupilStatus.LeaverNoticeGiven, updated!.Status);

        // Other three fields untouched.
        Assert.Equal("Rosalind Franklin", updated.Name);
        Assert.Equal(YearGroup.Year7, updated.YearGroup);
        Assert.Equal(BoardingType.Day, updated.BoardingType);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();

        var history = Assert.Single(editHistories);
        Assert.Equal(nameof(Pupil.Status), history.FieldName);
        Assert.Equal("Joiner", history.PreviousValue);
        Assert.Equal("Leaver — notice given", history.NewValue);
        Assert.Equal(ApplicationUser.SystemActorId, history.ActorId);
        Assert.Equal(DateTimeKind.Utc, history.ChangedAtUtc.Kind);
    }

    [Fact]
    public async Task UpdatePupilAsync_MultiFieldEdit_WritesExactlyOneRowPerChangedField()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Chien-Shiung Wu",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = YearGroup.Year9,
            BoardingType = pupil.BoardingType,
            Status = PupilStatus.LeaverNoticeGiven,
        });

        Assert.NotNull(updated);
        Assert.Equal(YearGroup.Year9, updated!.YearGroup);
        Assert.Equal(PupilStatus.LeaverNoticeGiven, updated.Status);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();

        Assert.Equal(2, editHistories.Count);

        var byField = editHistories.ToDictionary(h => h.FieldName);
        Assert.Equal("Year 7", byField[nameof(Pupil.YearGroup)].PreviousValue);
        Assert.Equal("Year 9", byField[nameof(Pupil.YearGroup)].NewValue);
        Assert.Equal("Joiner", byField[nameof(Pupil.Status)].PreviousValue);
        Assert.Equal("Leaver — notice given", byField[nameof(Pupil.Status)].NewValue);
    }

    [Fact]
    public async Task UpdatePupilAsync_NoOpSave_WritesZeroChangeHistoryRowsAndLeavesPupilUnchanged()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Marie Curie",
            YearGroup = YearGroup.Year8,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
        });

        var beforeHistoryCount = await db.ChangeHistories.CountAsync(ch => ch.PupilId == pupil.Id);

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
        });

        Assert.NotNull(updated);

        var afterHistoryCount = await db.ChangeHistories.CountAsync(ch => ch.PupilId == pupil.Id);
        Assert.Equal(beforeHistoryCount, afterHistoryCount);

        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.Equal("Marie Curie", reloaded.Name);
        Assert.Equal(YearGroup.Year8, reloaded.YearGroup);
        Assert.Equal(BoardingType.FullBoard, reloaded.BoardingType);
        Assert.Equal(PupilStatus.Joiner, reloaded.Status);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("YearGroup")]
    [InlineData("BoardingType")]
    [InlineData("Status")]
    public async Task UpdatePupilAsync_MissingRequiredField_ThrowsAndSavesNothing(string missingField)
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Dorothy Hodgkin",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        var request = new UpdatePupilRequest
        {
            Name = missingField == "Name" ? "" : "Dorothy Hodgkin (edited)",
            YearGroup = missingField == "YearGroup" ? null : YearGroup.Year10,
            BoardingType = missingField == "BoardingType" ? null : BoardingType.FullBoard,
            Status = missingField == "Status" ? null : PupilStatus.LeaverNoticeGiven,
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdatePupilAsync(pupil.Id, request));

        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.Equal("Dorothy Hodgkin", reloaded.Name);
        Assert.Equal(YearGroup.Year9, reloaded.YearGroup);
        Assert.Equal(BoardingType.Day, reloaded.BoardingType);
        Assert.Equal(PupilStatus.Joiner, reloaded.Status);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();
        Assert.Empty(editHistories);
    }

    [Fact]
    public async Task UpdatePupilAsync_UnknownId_ReturnsNull()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var result = await service.UpdatePupilAsync(999, new UpdatePupilRequest
        {
            Name = "Nobody",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdatePupilAsync_ConcurrentEditsToSamePupil_BothApplyWithoutError()
    {
        await using var seedDb = CreateContext();
        var seedService = new PupilService(seedDb);
        var pupil = await seedService.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Shared Pupil",
            YearGroup = YearGroup.Year10,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });

        // Two distinct DbContext/PupilService instances editing the same
        // pupil concurrently, as two staff submitting near-simultaneously
        // would produce — the static write lock serializes them so neither
        // surfaces a DbUpdateException, and the later write wins (AD-4).
        await using var dbA = CreateContext();
        await using var dbB = CreateContext();
        var serviceA = new PupilService(dbA);
        var serviceB = new PupilService(dbB);

        var requestA = new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = PupilStatus.ProvisionalLeaver,
        };
        var requestB = new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = PupilStatus.LeaverFeesInLieuDue,
        };

        var results = await Task.WhenAll(
            serviceA.UpdatePupilAsync(pupil.Id, requestA),
            serviceB.UpdatePupilAsync(pupil.Id, requestB));

        Assert.NotNull(results[0]);
        Assert.NotNull(results[1]);

        await using var verifyDb = CreateContext();
        var finalPupil = await verifyDb.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.True(
            finalPupil.Status is PupilStatus.ProvisionalLeaver or PupilStatus.LeaverFeesInLieuDue,
            $"Expected the last write to win with one of the two submitted statuses, but got {finalPupil.Status}.");

        // Both serialized writes must each leave their own ChangeHistory
        // row -- one write must never clobber or skip the other's history.
        var editHistories = await verifyDb.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .OrderBy(ch => ch.Id)
            .ToListAsync();

        Assert.Equal(2, editHistories.Count);
        Assert.All(editHistories, h => Assert.Equal(nameof(Pupil.Status), h.FieldName));
        Assert.Equal("Joiner", editHistories[0].PreviousValue);
        var expectedSecondPreviousValue = editHistories[0].NewValue;
        Assert.Equal(expectedSecondPreviousValue, editHistories[1].PreviousValue);
        Assert.Equal(finalPupil.Status.ToDisplayName(), editHistories[1].NewValue);
    }

    [Theory]
    [InlineData(PupilStatus.Joiner, true)]
    [InlineData(PupilStatus.PipelineOffered, false)]
    [InlineData(PupilStatus.DeferredPlace, false)]
    [InlineData(PupilStatus.ProvisionalLeaver, true)]
    [InlineData(PupilStatus.LeaverNoticeGiven, true)]
    [InlineData(PupilStatus.LeaverFeesInLieuDue, true)]
    public void CountsTowardRoll_MatchesTheSixValueMappingConfirmedWithHeadOfAdmissions(PupilStatus status, bool expected)
    {
        Assert.Equal(expected, status.CountsTowardRoll());
    }
}
