using System.ComponentModel.DataAnnotations;

namespace PupilAdmissions.Domain;

/// <summary>
/// A pupil's boarding arrangement. Always exactly one value per pupil —
/// never blank or ambiguous.
/// </summary>
public enum BoardingType
{
    [Display(Name = "Day")]
    Day = 0,

    [Display(Name = "Full Board")]
    FullBoard = 1,

    [Display(Name = "Weekly Board")]
    WeeklyBoard = 2,
}
