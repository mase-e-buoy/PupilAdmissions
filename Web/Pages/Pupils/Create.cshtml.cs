using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Web.Pages.Pupils;

/// <summary>
/// The single guided create-pupil flow (FR-1): name, year group, boarding
/// type, initial status — one screen, no wizard steps.
/// </summary>
public class CreateModel : PageModel
{
    private readonly PupilService _pupilService;

    public CreateModel(PupilService pupilService)
    {
        _pupilService = pupilService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var request = new CreatePupilRequest
        {
            Name = Input.Name,
            YearGroup = Input.YearGroup,
            BoardingType = Input.BoardingType,
            Status = Input.Status,
        };

        var pupil = await _pupilService.CreatePupilAsync(request, cancellationToken);

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
        [Display(Name = "Initial status")]
        public PupilStatus? Status { get; set; }
    }
}
