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
/// Covers the "missing required field" I/O matrix row at the Web layer:
/// an invalid submission must redisplay the page (never redirect) and must
/// not create a pupil, matching the shared error-summary partial's contract.
/// </summary>
public class CreateModelTests : IDisposable
{
    private readonly string _dbPath;

    public CreateModelTests()
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
    public async Task OnPostAsync_WithInvalidModelState_RedisplaysPageAndDoesNotCreatePupil()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new CreateModel(service)
        {
            Input = new CreateModel.InputModel
            {
                Name = string.Empty, // blank name -- fails [Required] the same way the real form validates
                YearGroup = YearGroup.Year7,
                BoardingType = BoardingType.Day,
                Status = PupilStatus.Joiner,
            },
        };
        model.ModelState.AddModelError("Input.Name", "Name is required.");

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.Empty(await db.Pupils.ToListAsync());
        Assert.Empty(await db.ChangeHistories.ToListAsync());
    }

    [Fact]
    public async Task OnPostAsync_WithValidModelState_CreatesPupilAndRedirectsToRoster()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new CreateModel(service)
        {
            Input = new CreateModel.InputModel
            {
                Name = "Alan Turing",
                YearGroup = YearGroup.Year11,
                BoardingType = BoardingType.WeeklyBoard,
                Status = PupilStatus.Joiner,
            },
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Index", redirect.PageName);
        Assert.Single(await db.Pupils.ToListAsync());
    }
}
