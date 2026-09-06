using System.ComponentModel.DataAnnotations;
using PupilAdmissions.Domain;

namespace PupilAdmissions.Application;

/// <summary>
/// Everything <c>PupilService.CreatePupilAsync</c> needs to create a pupil
/// (FR-1). Carries Data Annotations too so any caller (not only the Razor
/// Page's own bound input model) gets the same validation guarantee.
/// </summary>
public class CreatePupilRequest
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
