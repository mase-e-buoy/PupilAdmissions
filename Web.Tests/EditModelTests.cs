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

    /// <summary>
    /// Covers FR4's "fields appear ... for a short-stay pupil" acceptance
    /// criterion at the Web layer: GET must populate the Input model's
    /// short-stay toggle and its four fields from the persisted detail rows.
    /// </summary>
    [Fact]
    public async Task OnGetAsync_ShortStayPupil_PopulatesShortStayAndDetailFields()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Marco Polo",
            YearGroup = YearGroup.Year8,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "4 weeks",
            AgentName = "Silk Road Agents",
            DepositDetail = "Paid in full",
            Nationality = "Italian",
        });
        var model = new EditModel(service);

        var result = await model.OnGetAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.Input.IsShortStay);
        Assert.Equal("4 weeks", model.Input.LengthOfStay);
        Assert.Equal("Silk Road Agents", model.Input.AgentName);
        Assert.Equal("Paid in full", model.Input.DepositDetail);
        Assert.Equal("Italian", model.Input.Nationality);
    }

    /// <summary>
    /// Covers a non-short-stay pupil never showing the four detail fields
    /// (FR4): GET must leave them null even if they happened to be null in
    /// storage already (the pupil never had detail rows).
    /// </summary>
    [Fact]
    public async Task OnGetAsync_NonShortStayPupil_LeavesDetailFieldsNull()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Not Short Stay",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });
        var model = new EditModel(service);

        var result = await model.OnGetAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.Input.IsShortStay);
        Assert.Null(model.Input.LengthOfStay);
        Assert.Null(model.Input.AgentName);
        Assert.Null(model.Input.DepositDetail);
        Assert.Null(model.Input.Nationality);
    }

    /// <summary>
    /// Covers the "un-flag short-stay" I/O matrix row at the Web layer:
    /// unchecking short-stay and saving must delete the detail rows through
    /// the same single write path.
    /// </summary>
    [Fact]
    public async Task OnPostAsync_UnflagShortStay_DeletesDetailRowsAndRedirects()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Unflag Me",
            YearGroup = YearGroup.Year10,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = "3 weeks",
            AgentName = "Agent B",
        });
        var model = new EditModel(service)
        {
            Input = new EditModel.InputModel
            {
                Name = pupil.Name,
                YearGroup = pupil.YearGroup,
                BoardingType = pupil.BoardingType,
                Status = pupil.Status,
                IsShortStay = false,
            },
        };

        var result = await model.OnPostAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(await db.ShortStayDetails.Where(s => s.PupilId == pupil.Id).ToListAsync());
        Assert.Empty(await db.InternationalDetails.Where(i => i.PupilId == pupil.Id).ToListAsync());

        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.False(reloaded.IsShortStay);
    }

    /// <summary>
    /// Covers the "missing length-of-stay" I/O matrix row on Edit: checking
    /// short-stay without length-of-stay must redisplay without persisting
    /// any change.
    /// </summary>
    [Fact]
    public async Task OnPostAsync_ShortStayMissingLengthOfStay_RedisplaysPageAndDoesNotChangePupil()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        var pupil = await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Needs Length",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });
        var model = new EditModel(service)
        {
            Input = new EditModel.InputModel
            {
                Name = pupil.Name,
                YearGroup = pupil.YearGroup,
                BoardingType = pupil.BoardingType,
                Status = pupil.Status,
                IsShortStay = true,
                LengthOfStay = string.Empty,
            },
        };
        model.ModelState.AddModelError("Input.LengthOfStay", "Length of stay is required for short-stay pupils.");

        var result = await model.OnPostAsync(pupil.Id, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var reloaded = await db.Pupils.AsNoTracking().SingleAsync(p => p.Id == pupil.Id);
        Assert.False(reloaded.IsShortStay);
        Assert.Empty(await db.ShortStayDetails.Where(s => s.PupilId == pupil.Id).ToListAsync());
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
        var input = new EditModel.InputModel
        {
            Name = "Needs Length",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
            IsShortStay = true,
            LengthOfStay = string.Empty,
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EditModel.InputModel.LengthOfStay)));
    }
}
