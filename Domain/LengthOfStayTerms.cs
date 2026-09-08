using System.ComponentModel.DataAnnotations;

namespace PupilAdmissions.Domain;

/// <summary>
/// How many academic terms a short-stay pupil's arrangement covers.
/// Required on every <see cref="ShortStayDetail"/> (FR4) -- a short stay is
/// always measured as a whole number of terms, never free text.
/// </summary>
public enum LengthOfStayTerms
{
    [Display(Name = "1 term")]
    OneTerm = 1,

    [Display(Name = "2 terms")]
    TwoTerms = 2,

    [Display(Name = "3 terms")]
    ThreeTerms = 3,
}
