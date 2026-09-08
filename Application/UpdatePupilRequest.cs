using System.ComponentModel.DataAnnotations;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// Everything <c>PupilService.UpdatePupilAsync</c> needs to edit a pupil
/// (FR-2, FR4, FR6). Mirrors <see cref="CreatePupilRequest"/>'s annotated
/// properties, including <see cref="IValidatableObject"/>'s conditional
/// requirement that <see cref="LengthOfStay"/> is set whenever
/// <see cref="IsShortStay"/> is true, so both flows validate identically.
/// </summary>
public class UpdatePupilRequest : IValidatableObject
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
    /// are ignored server-side and any existing detail rows are deleted
    /// (FR4).
    /// </summary>
    public bool IsShortStay { get; set; }

    public string? LengthOfStay { get; set; }

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
