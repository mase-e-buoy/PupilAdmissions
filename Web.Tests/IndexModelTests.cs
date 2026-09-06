using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;
using PupilAdmissions.Infrastructure;
using PupilAdmissions.Web.Pages.Pupils;

namespace PupilAdmissions.Web.Tests;

/// <summary>
/// Covers the roster page the "appears immediately in that year group's
/// roster" AC is actually observed through: pupils grouped by year group,
/// filterable to a single year group.
/// </summary>
public class IndexModelTests : IDisposable
{
    private readonly string _dbPath;

    public IndexModelTests()
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

    private static async Task SeedAcrossYearGroupsAsync(PupilService service)
    {
        await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Zara Year9",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.Day,
            Status = PupilStatus.Joiner,
        });
        await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Amir Year9",
            YearGroup = YearGroup.Year9,
            BoardingType = BoardingType.FullBoard,
            Status = PupilStatus.Joiner,
        });
        await service.CreatePupilAsync(new CreatePupilRequest
        {
            Name = "Bea Year7",
            YearGroup = YearGroup.Year7,
            BoardingType = BoardingType.WeeklyBoard,
            Status = PupilStatus.PipelineOffered,
        });
    }

    [Fact]
    public async Task OnGetAsync_Unfiltered_GroupsAllPupilsByYearGroupInOrder()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        await SeedAcrossYearGroupsAsync(service);
        var model = new IndexModel(service);

        await model.OnGetAsync(yearGroup: null, CancellationToken.None);

        Assert.Null(model.YearGroupFilter);
        Assert.Equal(2, model.PupilsByYearGroup.Count);

        // Ordered by YearGroup ascending -- Year7 group before Year9 group.
        Assert.Equal(YearGroup.Year7, model.PupilsByYearGroup[0].Key);
        Assert.Equal(YearGroup.Year9, model.PupilsByYearGroup[1].Key);

        var year7Names = model.PupilsByYearGroup[0].Select(p => p.Name).ToList();
        Assert.Equal(new[] { "Bea Year7" }, year7Names);

        // Within a year group, GetRosterAsync orders by name.
        var year9Names = model.PupilsByYearGroup[1].Select(p => p.Name).ToList();
        Assert.Equal(new[] { "Amir Year9", "Zara Year9" }, year9Names);
    }

    [Fact]
    public async Task OnGetAsync_FilteredToOneYearGroup_ReturnsOnlyThatGroup()
    {
        await using var db = CreateContext();
        var service = new PupilService(db);
        await SeedAcrossYearGroupsAsync(service);
        var model = new IndexModel(service);

        await model.OnGetAsync(YearGroup.Year9, CancellationToken.None);

        Assert.Equal(YearGroup.Year9, model.YearGroupFilter);
        var group = Assert.Single(model.PupilsByYearGroup);
        Assert.Equal(YearGroup.Year9, group.Key);
        Assert.Equal(new[] { "Amir Year9", "Zara Year9" }, group.Select(p => p.Name).ToList());
    }
}
