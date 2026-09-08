using System.ComponentModel.DataAnnotations;
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

    /// <summary>
    /// Covers the short-stay toggle's happy path at the Web layer (FR4,
    /// FR6): checking it and populating all four detail fields must create
    /// both detail rows via the same single write path.
    /// </summary>
    [Fact]
    public async Task OnPostAsync_ShortStayWithDetailFields_CreatesPupilAndDetailRows()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new CreateModel(service)
        {
            Input = new CreateModel.InputModel
            {
                Name = "Marco Polo",
                YearGroup = YearGroup.Year8,
                BoardingType = BoardingType.FullBoard,
                Status = PupilStatus.Joiner,
                IsShortStay = true,
                LengthOfStay = LengthOfStayTerms.OneTerm,
                AgentName = "Silk Road Agents",
                DepositDetail = "Paid in full",
                Nationality = "Italian",
            },
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var pupil = Assert.Single(await db.Pupils.ToListAsync());
        Assert.True(pupil.IsShortStay);
        Assert.Equal(1, await db.ShortStayDetails.CountAsync(s => s.PupilId == pupil.Id));
        Assert.Equal(1, await db.InternationalDetails.CountAsync(i => i.PupilId == pupil.Id));
    }

    /// <summary>
    /// Covers the "missing length-of-stay" I/O matrix row: checking
    /// short-stay without length-of-stay must redisplay the page (never
    /// redirect) and must not create a pupil or any detail row.
    /// </summary>
    [Fact]
    public async Task OnPostAsync_ShortStayMissingLengthOfStay_RedisplaysPageAndDoesNotCreatePupil()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new CreateModel(service)
        {
            Input = new CreateModel.InputModel
            {
                Name = "No Length",
                YearGroup = YearGroup.Year7,
                BoardingType = BoardingType.Day,
                Status = PupilStatus.Joiner,
                IsShortStay = true,
                LengthOfStay = null,
            },
        };
        model.ModelState.AddModelError("Input.LengthOfStay", "Length of stay is required for short-stay pupils.");

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await db.Pupils.ToListAsync());
        Assert.Empty(await db.ShortStayDetails.ToListAsync());
    }

    /// <summary>
    /// Exercises the real <c>InputModel.Validate()</c> method (rather than
    /// simulating its effect via <c>ModelState.AddModelError</c>, which
    /// would pass even if the conditional-required check were broken) to
    /// confirm it actually flags a missing <c>LengthOfStay</c> when
    /// <c>IsShortStay</c> is true.
    /// </summary>
    [Fact]
    public void InputModel_ShortStayMissingLengthOfStay_RealValidationFlagsLengthOfStay()
    {
        var input = new CreateModel.InputModel
        {
            Name = "No Length",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = null,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateModel.InputModel.LengthOfStay)));
    }

    /// <summary>
    /// Covers non-short-stay create ignoring stray detail values
    /// server-side, not merely hiding them by CSS (FR4).
    /// </summary>
    [Fact]
    public async Task OnPostAsync_NonShortStayWithStrayDetailValues_IgnoresThemAndCreatesNoDetailRows()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var model = new CreateModel(service)
        {
            Input = new CreateModel.InputModel
            {
                Name = "Not Short Stay",
                YearGroup = YearGroup.Year9,
                BoardingType = BoardingType.Day,
                Status = PupilStatus.Joiner,
                IsShortStay = false,
                LengthOfStay = LengthOfStayTerms.ThreeTerms,
                AgentName = "Should also be ignored",
            },
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var pupil = Assert.Single(await db.Pupils.ToListAsync());
        Assert.False(pupil.IsShortStay);
        Assert.Empty(await db.ShortStayDetails.ToListAsync());
        Assert.Empty(await db.InternationalDetails.ToListAsync());
    }
}
