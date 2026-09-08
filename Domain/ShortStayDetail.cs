namespace PupilAdmissions.Domain;

/// <summary>
/// Length-of-stay detail for a short-stay pupil (0..1 per <see cref="Pupil"/>,
/// AD-9). Holds only <see cref="LengthOfStay"/> plus a reference to the same
/// <see cref="InternationalDetail"/> row used when the pupil is also
/// international — agent name, deposit detail, and nationality are never
/// duplicated here.
/// </summary>
public class ShortStayDetail
{
    public int Id { get; set; }

    public int PupilId { get; set; }

    public Pupil? Pupil { get; set; }

    public string LengthOfStay { get; set; } = string.Empty;

    public int? InternationalDetailId { get; set; }

    public InternationalDetail? InternationalDetail { get; set; }
}
