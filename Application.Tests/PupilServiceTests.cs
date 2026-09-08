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

    // --- Story 2.3: short-stay and international/agent details (AD-9, FR4, FR6) ---

    [Fact]
    public async Task CreatePupilAsync_ShortStayWithAllDetailFields_CreatesDetailRowsAndWritesEightChangeHistoryRows()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Malala Yousafzai",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "6 weeks",
            AgentName = "Global Agents Ltd",
            DepositDetail = "GBP 500 paid",
            Nationality = "Pakistani",
        });

        var shortStayDetail = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal("6 weeks", shortStayDetail.LengthOfStay);
        Assert.NotNull(shortStayDetail.InternationalDetailId);

        var internationalDetail = await db.InternationalDetails.AsNoTracking().SingleAsync(i => i.PupilId == pupil.Id);
        Assert.Equal("Global Agents Ltd", internationalDetail.AgentName);
        Assert.Equal("GBP 500 paid", internationalDetail.DepositDetail);
        Assert.Equal("Pakistani", internationalDetail.Nationality);
        Assert.Equal(shortStayDetail.InternationalDetailId, internationalDetail.Id);

        var histories = await db.ChangeHistories.Where(ch => ch.PupilId == pupil.Id).ToListAsync();
        Assert.Equal(8, histories.Count);

        var byField = histories.ToDictionary(h => h.FieldName);
        Assert.Equal("6 weeks", byField[nameof(ShortStayDetail.LengthOfStay)].NewValue);
        Assert.Equal("Global Agents Ltd", byField[nameof(InternationalDetail.AgentName)].NewValue);
        Assert.Equal("GBP 500 paid", byField[nameof(InternationalDetail.DepositDetail)].NewValue);
        Assert.Equal("Pakistani", byField[nameof(InternationalDetail.Nationality)].NewValue);

        // IsShortStay itself is never its own ChangeHistory field (AD-9/FR4).
        Assert.False(byField.ContainsKey(nameof(Pupil.IsShortStay)));
    }

    [Fact]
    public async Task CreatePupilAsync_NonShortStay_WritesOnlyFourChangeHistoryRowsAndNoDetailRows()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Isaac Newton",
            YearGroup = YearGroup.Year8,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = false,
            // Detail fields submitted anyway (e.g. stale form state) -- must
            // be ignored server-side, not merely hidden by CSS (FR4).
            LengthOfStay = "2 weeks",
            AgentName = "Should Be Ignored",
        });

        Assert.False(await db.ShortStayDetails.AnyAsync(s => s.PupilId == pupil.Id));
        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));

        var histories = await db.ChangeHistories.Where(ch => ch.PupilId == pupil.Id).ToListAsync();
        Assert.Equal(4, histories.Count);
    }

    [Fact]
    public async Task CreatePupilAsync_ShortStayMissingLengthOfStay_ThrowsAndSavesNothing()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Missing LengthOfStay",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = null,
        }));

        Assert.Empty(await db.Pupils.ToListAsync());
        Assert.Empty(await db.ShortStayDetails.ToListAsync());
        Assert.Empty(await db.ChangeHistories.ToListAsync());
    }

    /// <summary>
    /// Regression test: a stray over-200-character value left in a detail
    /// field (e.g. a JS-hidden field still submitted in the POST) must be
    /// silently ignored server-side when <c>IsShortStay</c> is false, not
    /// raise a <see cref="ValidationException"/> -- length limits on the
    /// four detail fields only apply when short-stay is actually checked.
    /// </summary>
    [Fact]
    public async Task CreatePupilAsync_NonShortStayWithOverLengthStrayValue_DoesNotThrow()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Stray Over-Length Value",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = false,
            AgentName = new string('x', 250),
        });

        Assert.True(pupil.Id > 0);
        Assert.False(await db.ShortStayDetails.AnyAsync(s => s.PupilId == pupil.Id));
        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));
    }

    [Fact]
    public async Task UpdatePupilAsync_IndependentFieldClear_ClearsOnlyThatFieldWithOneChangeHistoryRow()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Independent Clear",
            YearGroup = YearGroup.Year10,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "1 term",
            AgentName = "Agent A",
            Nationality = "French",
        });

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = true,
            LengthOfStay = "1 term",
            AgentName = null, // clear agent only
            DepositDetail = null,
            Nationality = "French", // untouched
        });

        Assert.NotNull(updated);

        var internationalDetail = await db.InternationalDetails.AsNoTracking().SingleAsync(i => i.PupilId == pupil.Id);
        Assert.Null(internationalDetail.AgentName);
        Assert.Null(internationalDetail.DepositDetail);
        Assert.Equal("French", internationalDetail.Nationality);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();

        var history = Assert.Single(editHistories);
        Assert.Equal(nameof(InternationalDetail.AgentName), history.FieldName);
        Assert.Equal("Agent A", history.PreviousValue);
        Assert.Null(history.NewValue);
    }

    [Fact]
    public async Task UpdatePupilAsync_UnflagShortStay_DeletesDetailRowsAndRecordsOneClearHistoryRowPerPopulatedField()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Unflag Me",
            YearGroup = YearGroup.Year11,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "3 weeks",
            AgentName = "Agent B",
            DepositDetail = "Deposit paid",
            Nationality = "Spanish",
        });

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = false,
        });

        Assert.NotNull(updated);
        Assert.False(updated!.IsShortStay);

        Assert.False(await db.ShortStayDetails.AnyAsync(s => s.PupilId == pupil.Id));
        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();

        Assert.Equal(4, editHistories.Count);
        Assert.All(editHistories, h => Assert.Null(h.NewValue));

        var byField = editHistories.ToDictionary(h => h.FieldName);
        Assert.Equal("3 weeks", byField[nameof(ShortStayDetail.LengthOfStay)].PreviousValue);
        Assert.Equal("Agent B", byField[nameof(InternationalDetail.AgentName)].PreviousValue);
        Assert.Equal("Deposit paid", byField[nameof(InternationalDetail.DepositDetail)].PreviousValue);
        Assert.Equal("Spanish", byField[nameof(InternationalDetail.Nationality)].PreviousValue);
    }

    [Fact]
    public async Task UpdatePupilAsync_FlagPreviouslyNonShortStayPupilAsShortStay_CreatesDetailRows()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Newly Short-Stay",
            YearGroup = YearGroup.Year6,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = false,
        });

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = true,
            LengthOfStay = "10 days",
        });

        Assert.NotNull(updated);
        Assert.True(updated!.IsShortStay);

        var shortStayDetail = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal("10 days", shortStayDetail.LengthOfStay);
        Assert.Null(shortStayDetail.InternationalDetailId);
        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue == null && ch.FieldName == nameof(ShortStayDetail.LengthOfStay))
            .ToListAsync();
        var history = Assert.Single(editHistories);
        Assert.Equal("10 days", history.NewValue);
    }

    [Fact]
    public async Task UpdatePupilAsync_ShortStayMissingLengthOfStay_ThrowsAndLeavesPupilUnchanged()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Still Needs Length",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "5 weeks",
        });

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = true,
            LengthOfStay = null,
        }));

        var reloaded = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal("5 weeks", reloaded.LengthOfStay);
    }

    [Fact]
    public async Task UpdatePupilAsync_ClearAllThreeInternationalFieldsWhileStillShortStay_DeletesInternationalDetailButKeepsShortStayDetail()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Clear All Three",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "2 terms",
            AgentName = "Agent C",
            DepositDetail = "Deposit C",
            Nationality = "German",
        });

        var exception = await Record.ExceptionAsync(() => service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = true,
            LengthOfStay = "2 terms",
            AgentName = null,
            DepositDetail = null,
            Nationality = null,
        }));

        Assert.Null(exception);

        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));

        var shortStayDetail = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal("2 terms", shortStayDetail.LengthOfStay);
        Assert.Null(shortStayDetail.InternationalDetailId);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();

        // LengthOfStay unchanged -- only the three cleared international
        // fields produce history rows.
        Assert.Equal(3, editHistories.Count);
        Assert.All(editHistories, h => Assert.Null(h.NewValue));
    }

    [Fact]
    public async Task UpdatePupilAsync_AddFirstInternationalFieldToExistingShortStayPupil_CreatesInternationalDetail()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        // Already short-stay, but never had any agent/deposit/nationality
        // value -- no InternationalDetail row exists yet.
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "First International Field",
            YearGroup = YearGroup.Year10,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "1 month",
        });

        Assert.False(await db.InternationalDetails.AnyAsync(i => i.PupilId == pupil.Id));

        var updated = await service.UpdatePupilAsync(pupil.Id, new UpdatePupilRequest
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
            IsShortStay = true,
            LengthOfStay = "1 month",
            AgentName = "Agent D",
        });

        Assert.NotNull(updated);

        var internationalDetail = await db.InternationalDetails.AsNoTracking().SingleAsync(i => i.PupilId == pupil.Id);
        Assert.Equal("Agent D", internationalDetail.AgentName);
        Assert.Null(internationalDetail.DepositDetail);
        Assert.Null(internationalDetail.Nationality);

        var shortStayDetail = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal(internationalDetail.Id, shortStayDetail.InternationalDetailId);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue == null && ch.FieldName == nameof(InternationalDetail.AgentName))
            .ToListAsync();
        var history = Assert.Single(editHistories);
        Assert.Equal("Agent D", history.NewValue);
    }

    [Fact]
    public async Task CreatePupilAsync_DetailFieldsWithSurroundingWhitespace_AreTrimmedBeforeStorage()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);

        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Trim Me",
            YearGroup = YearGroup.Year8,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "  6 weeks  ",
            AgentName = " Agent A ",
            DepositDetail = " Deposit A ",
            Nationality = " French ",
        });

        var shortStayDetail = await db.ShortStayDetails.AsNoTracking().SingleAsync(s => s.PupilId == pupil.Id);
        Assert.Equal("6 weeks", shortStayDetail.LengthOfStay);

        var internationalDetail = await db.InternationalDetails.AsNoTracking().SingleAsync(i => i.PupilId == pupil.Id);
        Assert.Equal("Agent A", internationalDetail.AgentName);
        Assert.Equal("Deposit A", internationalDetail.DepositDetail);
        Assert.Equal("French", internationalDetail.Nationality);
    }
}
