namespace NcsScheduler.Models.ViewModels;

public class ImportRowViewModel
{
    public string Callsign { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? MemberNumber { get; set; }
}

public class InvalidImportRow
{
    public ImportRowViewModel Row { get; set; } = new();
    public string Error { get; set; } = "";
}

public class ImportPreviewViewModel
{
    public List<ImportRowViewModel> ValidRows { get; set; } = [];
    public List<InvalidImportRow> InvalidRows { get; set; } = [];
}
