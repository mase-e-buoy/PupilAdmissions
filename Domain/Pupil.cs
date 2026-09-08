namespace PupilAdmissions.Domain;

/// <summary>
/// A pupil record. Story 2.3 adds <see cref="IsShortStay"/> plus the
/// <see cref="ShortStayDetail"/>/<see cref="InternationalDetail"/> 0..1
/// navigations it gates (FR4, FR6, AD-9) — discount and archival fields
/// remain later stories (2.4/2.6) and are deliberately not modeled here yet.
/// </summary>
public class Pupil
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public YearGroup YearGroup { get; set; }

    public BoardingType BoardingType { get; set; }

    public PupilStatus Status { get; set; }

    /// <summary>
    /// Whether this pupil is short-stay. Gates whether
    /// <see cref="ShortStayDetail"/>/<see cref="InternationalDetail"/> may
    /// exist for this pupil — the toggle is the only entry point into
    /// <see cref="InternationalDetail"/> (FR4).
    /// </summary>
    public bool IsShortStay { get; set; }

    public ShortStayDetail? ShortStayDetail { get; set; }

    public InternationalDetail? InternationalDetail { get; set; }

    public ICollection<ChangeHistory> ChangeHistories { get; set; } = new List<ChangeHistory>();
}
