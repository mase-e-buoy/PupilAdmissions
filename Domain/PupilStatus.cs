using System.ComponentModel.DataAnnotations;

namespace PupilAdmissions.Domain;

/// <summary>
/// The single canonical status field for a pupil (AD-1). Exactly the six
/// PRD-enumerated values — never signalled by colour, font, or any other
/// display-only styling, and never duplicated as a parallel representation
/// anywhere else in the system.
/// </summary>
/// <remarks>
/// PRD labels contain spaces/dashes that are not valid C# identifiers
/// (e.g. "Pipeline (offered)", "Leaver — notice given"), so members use
/// identifier-safe names with a <see cref="DisplayAttribute"/> carrying the
/// exact PRD label for display.
/// </remarks>
public enum PupilStatus
{
    [Display(Name = "Joiner")]
    Joiner,

    [Display(Name = "Pipeline (offered)")]
    PipelineOffered,

    [Display(Name = "Deferred place")]
    DeferredPlace,

    [Display(Name = "Provisional leaver")]
    ProvisionalLeaver,

    [Display(Name = "Leaver — notice given")]
    LeaverNoticeGiven,

    [Display(Name = "Leaver — fees in lieu due")]
    LeaverFeesInLieuDue,
}

/// <summary>
/// Single lookup for status semantics (AD-1) — the only place "does this
/// status count toward roll" is decided. <c>ReportingService</c> (Epic 3)
/// is the sole intended consumer; no other feature should re-derive this.
/// </summary>
public static class PupilStatusExtensions
{
    /// <summary>
    /// True for statuses that still count toward the physical roll
    /// (still attending until archived per AD-7): Joiner, Provisional
    /// leaver, Leaver — notice given, Leaver — fees in lieu due.
    /// False for statuses that carry only a target/future year group and
    /// are not yet on roll: Pipeline (offered), Deferred place.
    /// </summary>
    public static bool CountsTowardRoll(this PupilStatus status) => status switch
    {
        PupilStatus.Joiner => true,
        PupilStatus.ProvisionalLeaver => true,
        PupilStatus.LeaverNoticeGiven => true,
        PupilStatus.LeaverFeesInLieuDue => true,
        PupilStatus.PipelineOffered => false,
        PupilStatus.DeferredPlace => false,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };
}
