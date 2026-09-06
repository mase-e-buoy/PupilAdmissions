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
