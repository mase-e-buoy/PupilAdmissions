using System.ComponentModel.DataAnnotations;

namespace PupilAdmissions.Domain;

/// <summary>
/// The nine fixed year-group cohorts a pupil can belong to (PRD Glossary).
/// Modeled as an enum rather than free text or an unbounded int so every
/// layer reads the same closed set of values.
/// </summary>
public enum YearGroup
{
    [Display(Name = "Year 5")]
    Year5 = 5,

    [Display(Name = "Year 6")]
    Year6 = 6,

    [Display(Name = "Year 7")]
    Year7 = 7,

    [Display(Name = "Year 8")]
    Year8 = 8,

    [Display(Name = "Year 9")]
    Year9 = 9,

    [Display(Name = "Year 10")]
    Year10 = 10,

    [Display(Name = "Year 11")]
    Year11 = 11,

    [Display(Name = "Year 12")]
    Year12 = 12,

    [Display(Name = "Year 13")]
    Year13 = 13,
}
