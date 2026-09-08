using System.ComponentModel.DataAnnotations;

namespace PupilAdmissions.Domain;

/// <summary>
/// Shared helper for reading the exact PRD label off an enum member's
/// <see cref="DisplayAttribute"/> — used by both persistence (readable
/// SQLite values) and the UI (option labels), so there is exactly one
/// place that maps an enum value to its display text.
/// </summary>
public static class EnumDisplayExtensions
{
    public static string ToDisplayName(this YearGroup yearGroup) => ToDisplayName((Enum)yearGroup);

    public static string ToDisplayName(this BoardingType boardingType) => ToDisplayName((Enum)boardingType);

    public static string ToDisplayName(this PupilStatus status) => ToDisplayName((Enum)status);

    public static string ToDisplayName(this LengthOfStayTerms terms) => ToDisplayName((Enum)terms);

    private static string ToDisplayName(Enum value)
    {
        var member = value.GetType().GetField(value.ToString());
        var display = member?.GetCustomAttributes(typeof(DisplayAttribute), false)
            .OfType<DisplayAttribute>()
            .FirstOrDefault();
        return display?.Name ?? value.ToString();
    }
}
