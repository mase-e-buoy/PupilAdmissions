using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Web.Pages.Pupils;

/// <summary>
/// The single guided edit-pupil flow (FR-2): name, year group, boarding
/// type, status — the same one-screen shape as <see cref="CreateModel"/>,
/// pre-populated with the pupil's current values.
/// </summary>
public class EditModel : PageModel
{
    private readonly PupilService _pupilService;

    public EditModel(PupilService pupilService)
    {
        _pupilService = pupilService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var pupil = await _pupilService.GetByIdAsync(id, cancellationToken);
        if (pupil is null)
        {
            return NotFound();
        }

        Input = new InputModel
        {
            Name = pupil.Name,
            YearGroup = pupil.YearGroup,
            BoardingType = pupil.BoardingType,
            Status = pupil.Status,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        // Check existence before ModelState: a POST to a nonexistent id must
        // always 404, even when the submitted form also fails validation.
        if (await _pupilService.GetByIdAsync(id, cancellationToken) is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var request = new UpdatePupilRequest
        {
            Name = Input.Name,
            YearGroup = Input.YearGroup,
            BoardingType = Input.BoardingType,
            Status = Input.Status,
        };

        var pupil = await _pupilService.UpdatePupilAsync(id, request, cancellationToken);
        if (pupil is null)
        {
            return NotFound();
        }

        return RedirectToPage("Index", new { yearGroup = pupil.YearGroup });
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Name is required.")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Name must be between 1 and 200 characters.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Year group is required.")]
        [EnumDataType(typeof(YearGroup), ErrorMessage = "Year group must be a valid value.")]
        [Display(Name = "Year group")]
        public YearGroup? YearGroup { get; set; }

        [Required(ErrorMessage = "Boarding type is required.")]
        [EnumDataType(typeof(BoardingType), ErrorMessage = "Boarding type must be a valid value.")]
        [Display(Name = "Boarding type")]
        public BoardingType? BoardingType { get; set; }

        [Required(ErrorMessage = "Status is required.")]
        [EnumDataType(typeof(PupilStatus), ErrorMessage = "Status must be a valid value.")]
        [Display(Name = "Status")]
        public PupilStatus? Status { get; set; }
    }
}
