using NcsScheduler.Models.Domain;

namespace NcsScheduler.Models.ViewModels;

public class HolidayIndexViewModel
{
    public int SelectedYear { get; set; }
    public List<Holiday> Holidays { get; set; } = [];
    public List<int> AvailableYears { get; set; } = [];
}
