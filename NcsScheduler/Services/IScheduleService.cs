using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;

namespace NcsScheduler.Services;

public interface IScheduleService
{
    Task GenerateSessionsAsync(int netId, int weeksAhead = 8);
    Task GenerateAllSessionsAsync(int weeksAhead = 8);
    Task<SlotResolution> ResolveSlotAsync(int netSessionId);
    Task<ScheduleViewModel> GetPublicScheduleAsync(DateOnly from, DateOnly to);
}

public record SlotResolution(
    NetController? Controller,
    AssignmentType? AssignmentType,
    bool NeedsNcs,
    bool HasVolunteer
);
