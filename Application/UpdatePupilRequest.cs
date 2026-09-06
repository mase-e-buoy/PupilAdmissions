using System.ComponentModel.DataAnnotations;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// Everything <c>PupilService.UpdatePupilAsync</c> needs to edit a pupil
/// (FR-2). Mirrors <see cref="CreatePupilRequest"/>'s four annotated
/// properties so both flows validate identically.
/// </summary>
public class UpdatePupilRequest
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
}
