using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PupilAdmissions.Application;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Web.Pages.Pupils;

/// <summary>
/// The single guided create-pupil flow (FR-1, FR4, FR6): name, year group,
/// boarding type, initial status, plus the short-stay toggle and its four
/// conditional fields (length-of-stay, agent, deposit, nationality) — one
/// screen, no wizard steps.
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
            IsShortStay = Input.IsShortStay,
            LengthOfStay = Input.LengthOfStay,
            AgentName = Input.AgentName,
            DepositDetail = Input.DepositDetail,
            Nationality = Input.Nationality,
        };

        var pupil = await _pupilService.CreatePupilAsync(request, cancellationToken);

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
        [Display(Name = "Initial status")]
        public PupilStatus? Status { get; set; }

        /// <summary>
        /// Reveals the four fields below only when checked (FR4); when
        /// unchecked, any submitted values for them are ignored server-side,
        /// not merely hidden by CSS.
        /// </summary>
        [Display(Name = "Short-stay")]
        public bool IsShortStay { get; set; }

        [Display(Name = "Length of stay")]
        public string? LengthOfStay { get; set; }

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

            if (string.IsNullOrWhiteSpace(LengthOfStay))
            {
                yield return new ValidationResult(
                    "Length of stay is required for short-stay pupils.",
                    new[] { nameof(LengthOfStay) });
            }
            else if (LengthOfStay.Length > 200)
            {
                yield return new ValidationResult(
                    "Length of stay must be at most 200 characters.",
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
