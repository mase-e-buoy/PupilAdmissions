using System.ComponentModel.DataAnnotations;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// Everything <c>PupilService.CreatePupilAsync</c> needs to create a pupil
/// (FR-1, FR4, FR6). Carries Data Annotations too so any caller (not only
/// the Razor Page's own bound input model) gets the same validation
/// guarantee. Implements <see cref="IValidatableObject"/> so
/// <see cref="LengthOfStay"/> is required exactly when
/// <see cref="IsShortStay"/> is true — the four short-stay/international
/// fields are otherwise independently optional (FR6).
/// </summary>
public class CreatePupilRequest : IValidatableObject
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EnumDataType(typeof(YearGroup))]
    public YearGroup? YearGroup { get; set; }

    [Required]
    [EnumDataType(typeof(BoardingType))]
    public BoardingType? BoardingType { get; set; }

    [Required]
    [EnumDataType(typeof(PupilStatus))]
    public PupilStatus? Status { get; set; }

    /// <summary>
    /// Whether this pupil is short-stay. Only when true are the four
    /// detail fields below read; when false any submitted values for them
    /// are ignored server-side (FR4).
    /// </summary>
    public bool IsShortStay { get; set; }

    public LengthOfStayTerms? LengthOfStay { get; set; }

    public string? AgentName { get; set; }

    public string? DepositDetail { get; set; }

    public string? Nationality { get; set; }

    /// <summary>
    /// The four detail fields' length limits are only enforced when
    /// <see cref="IsShortStay"/> is true -- a stray over-length value left
    /// in a field after unchecking short-stay must be ignored server-side,
    /// not raise a validation error (FR4). A plain <c>[StringLength(200)]</c>
    /// on the property would apply unconditionally regardless of
    /// <see cref="IsShortStay"/>, so the checks live here instead.
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
