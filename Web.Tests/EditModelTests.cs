using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;
using PupilAdmissions.Infrastructure;
using PupilAdmissions.Web.Pages.Pupils;

namespace PupilAdmissions.Web.Tests;

/// <summary>
/// Covers the Edit page's I/O matrix rows: found/not-found on GET, and on
/// POST an invalid submission redisplays without persisting while a valid
/// one persists and redirects, mirroring <see cref="CreateModelTests"/>.
/// </summary>
public class EditModelTests : IDisposable
{
    private readonly string _dbPath;

    public EditModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pupiladmissions-webtests-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
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

    [Fact]
    public async Task OnGetAsync_ExistingPupil_PopulatesInputWithCurrentValues()
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
        var model = new EditModel(service);

        var result = await model.OnGetAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal("Ada Lovelace", model.Input.Name);
        Assert.Equal(YearGroup.Year7, model.Input.YearGroup);
        Assert.Equal(BoardingType.Day, model.Input.BoardingType);
        Assert.Equal(PupilStatus.Joiner, model.Input.Status);
    }

    [Fact]
    public async Task OnGetAsync_NonexistentPupil_ReturnsNotFound()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new EditModel(service);

        var result = await model.OnGetAsync(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostAsync_NonexistentPupil_ReturnsNotFound()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new EditModel(service)
        {
            Input = new EditModel.InputModel
            {
                Name = "Someone",
                YearGroup = YearGroup.Year7,
                BoardingType = BoardingType.Day,
                Status = PupilStatus.Joiner,
            },
        };

        var result = await model.OnPostAsync(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task OnPostAsync_WithInvalidModelState_RedisplaysPageAndDoesNotChangePupil()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Grace Hopper",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
        });
        var model = new EditModel(service)
        {
            Input = new EditModel.InputModel
            {
                Name = string.Empty, // blank name -- fails [Required] the same way the real form validates
                YearGroup = YearGroup.Year9,
                BoardingType = BoardingType.FullBoard,
                Status = PupilStatus.LeaverNoticeGiven,
            },
        };
        model.ModelState.AddModelError("Input.Name", "Name is required.");

        var result = await model.OnPostAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);

        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.Equal("Grace Hopper", reloaded.Name);
        Assert.Equal(PupilStatus.Joiner, reloaded.Status);

        var editHistories = await db.ChangeHistories
            .Where(ch => ch.PupilId == pupil.Id && ch.PreviousValue != null)
            .ToListAsync();
        Assert.Empty(editHistories);
    }

    [Fact]
    public async Task OnPostAsync_WithValidModelState_UpdatesPupilAndRedirectsToRoster()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Alan Turing",
            YearGroup = YearGroup.Year11,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.Joiner,
        });
        var model = new EditModel(service)
        {
            Input = new EditModel.InputModel
            {
                Name = "Alan Turing",
                YearGroup = YearGroup.Year11,
                BoardingType = BoardingType.WeeklyBoard,
                Status = PupilStatus.LeaverNoticeGiven,
            },
        };

        var result = await model.OnPostAsync(pupil.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);

        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.Equal(PupilStatus.LeaverNoticeGiven, reloaded.Status);
    }
}
