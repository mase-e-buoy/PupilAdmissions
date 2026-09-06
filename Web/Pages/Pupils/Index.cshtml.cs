using Microsoft.AspNetCore.Mvc.RazorPages;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Web.Pages.Pupils;

/// <summary>
/// Roster view, filterable/grouped by year group, so a newly created
/// pupil's immediate appearance is observable (AC).
/// </summary>
public class IndexModel : PageModel
{
    private readonly PupilService _pupilService;

    public IndexModel(PupilService pupilService)
    {
        _pupilService = pupilService;
    }

    public YearGroup? YearGroupFilter { get; set; }

    public IReadOnlyList<IGrouping<YearGroup, Pupil>> PupilsByYearGroup { get; set; } = Array.Empty<IGrouping<YearGroup, Pupil>>();

    public async Task OnGetAsync(YearGroup? yearGroup, CancellationToken cancellationToken)
    {
        YearGroupFilter = yearGroup;
        var pupils = await _pupilService.GetRosterAsync(yearGroup, cancellationToken);
        PupilsByYearGroup = pupils
            .GroupBy(p => p.YearGroup)
            .OrderBy(g => g.Key)
            .ToList();
    }
}
