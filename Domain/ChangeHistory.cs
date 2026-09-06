namespace PupilAdmissions.Domain;

/// <summary>
/// One row per populated field changed on a <see cref="Pupil"/> (AD-3) —
/// never one composite row per save. Every row has a non-null actor and a
/// UTC timestamp (FR8).
/// </summary>
public class ChangeHistory
{
    public int Id { get; set; }

    public int PupilId { get; set; }

    public Pupil? Pupil { get; set; }

    /// <summary>The name of the field that changed, e.g. "Name", "YearGroup".</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>The value before this change, or null when the field was previously unset.</summary>
    public string? PreviousValue { get; set; }

    /// <summary>The value after this change.</summary>
    public string? NewValue { get; set; }

    /// <summary>The acting <see cref="ApplicationUser"/>. Always set — never anonymous (FR8).</summary>
    public int ActorId { get; set; }

    public ApplicationUser? Actor { get; set; }

    /// <summary>UTC timestamp of the change (FR8).</summary>
    public DateTime ChangedAtUtc { get; set; }
}
