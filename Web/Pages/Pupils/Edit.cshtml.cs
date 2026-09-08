using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Web.Pages.Pupils;

/// <summary>
/// The single guided edit-pupil flow (FR-2, FR4, FR6): name, year group,
/// boarding type, status, plus the short-stay toggle and its four
/// conditional fields — the same one-screen shape as <see cref="CreateModel"/>,
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
            IsShortStay = pupil.IsShortStay,
            LengthOfStay = pupil.ShortStayDetail?.LengthOfStay,
            AgentName = pupil.InternationalDetail?.AgentName,
            DepositDetail = pupil.InternationalDetail?.DepositDetail,
            Nationality = pupil.InternationalDetail?.Nationality,
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
            IsShortStay = Input.IsShortStay,
            LengthOfStay = Input.LengthOfStay,
            AgentName = Input.AgentName,
            DepositDetail = Input.DepositDetail,
            Nationality = Input.Nationality,
        };

        var pupil = await _pupilService.UpdatePupilAsync(id, request, cancellationToken);
        if (pupil is null)
        {
            return NotFound();
        }

        return RedirectToPage("Index", new { yearGroup = pupil.YearGroup });
    }

    public class InputModel : IValidatableObject
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

        /// <summary>
        /// Reveals the four fields below only when checked (FR4); when
        /// unchecked, any submitted values for them are ignored server-side,
        /// not merely hidden by CSS.
        /// </summary>
        [Display(Name = "Short-stay")]
        public bool IsShortStay { get; set; }

        [Display(Name = "Length of stay")]
        public LengthOfStayTerms? LengthOfStay { get; set; }

        [Display(Name = "Agent name")]
        public string? AgentName { get; set; }

        [Display(Name = "Deposit detail")]
        public string? DepositDetail { get; set; }

        [Display(Name = "Nationality")]
        public string? Nationality { get; set; }

        /// <summary>
        /// The four detail fields' required/length checks are only enforced
        /// when <see cref="IsShortStay"/> is true -- a stray over-length
        /// value left in a field after unchecking short-stay must be
        /// ignored server-side, not raise a validation error (FR4). A plain
        /// <c>[StringLength(200)]</c> on the property would apply
        /// unconditionally regardless of <see cref="IsShortStay"/>, so the
        /// checks live here instead.
        /// </summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!IsShortStay)
            {
                yield break;
            }

            if (LengthOfStay is null)
            {
                yield return new ValidationResult(
                    "Length of stay is required for short-stay pupils.",
                    new[] { nameof(LengthOfStay) });
            }

            if (AgentName?.Length > 200)
            {
                yield return new ValidationResult("Agent name must be at most 200 characters.", new[] { nameof(AgentName) });
            }

            if (DepositDetail?.Length > 200)
            {
                yield return new ValidationResult("Deposit detail must be at most 200 characters.", new[] { nameof(DepositDetail) });
            }

            if (Nationality?.Length > 200)
            {
                yield return new ValidationResult("Nationality must be at most 200 characters.", new[] { nameof(Nationality) });
            }
        }
    }
}
