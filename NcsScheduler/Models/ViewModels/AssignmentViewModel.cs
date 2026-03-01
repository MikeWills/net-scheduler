using NcsScheduler.Models.Domain;

namespace NcsScheduler.Models.ViewModels;

public class AssignmentSlotItem
{
    public NetSession Session { get; set; } = null!;
    public NetController? StandingController { get; set; }
    public List<SessionAssignment> Volunteers { get; set; } = [];
}
