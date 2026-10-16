using System.Text.Json;
using SierraBusiness.Domain;
namespace SierraBusiness.Desktop;
public sealed record CompanyInfo(Guid Id, string Name, string DataDirectory);
/// <summary>Separate company databases and local Identity stores; existing installations retain their original directory.</summary>
public sealed class CompanyDirectory(string legacyDataDirectory)
{
    public CompanyInfo Default => new(Guid.Empty, DisplayName("Original company",Path.GetFullPath(legacyDataDirectory)), Path.GetFullPath(legacyDataDirectory));
    private static string DisplayName(string name,string directory)
    {
        if (!CompanyPeriodService.IsArchive(directory)) return name;
        var marker = JsonSerializer.Deserialize<CompanyPeriodMarker>(File.ReadAllText(Path.Combine(directory,CompanyPeriodService.MarkerFile)))!;
        return name+" — archive through "+marker.Cutoff.ToString("yyyy-MM-dd")+(marker.LaterDocuments > 0 ? $" (also holds {marker.LaterDocuments} later document{(marker.LaterDocuments == 1 ? "" : "s")} for reference)" : "");
    }
    private string Root => Path.Combine(Path.GetFullPath(legacyDataDirectory), "Companies");
    public IReadOnlyList<CompanyInfo> List()
    {
        var companies = new List<CompanyInfo> { Default };
        if (!Directory.Exists(Root)) return companies;
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var id)) continue;
            if (File.Exists(Path.Combine(directory, ".pending"))) continue;
            var metadata = Path.Combine(directory, "company.json");
            if (!File.Exists(metadata)) continue;
            try
            {
                var name = JsonSerializer.Deserialize<string>(File.ReadAllText(metadata));
                if (!string.IsNullOrWhiteSpace(name)) companies.Add(new(id, DisplayName(name,directory), directory));
            }
            catch (JsonException) { /* A damaged metadata file must not hide other companies. */ }
        }
        return companies;
    }
    public CompanyInfo Resolve(Guid id) => List().SingleOrDefault(x => x.Id == id) ?? throw new BusinessException("Company not found. Reopen Rokela Business Admin without the company argument to select an existing company.");
    internal void Publish(Guid id) => File.Delete(Path.Combine(Root,id.ToString("N"),".pending"));
    public CompanyInfo Create(string name, bool pending = false)
    {
        name = Rules.Required(name, "Company name", 120);
        var id = Guid.NewGuid(); var directory = Path.Combine(Root, id.ToString("N")); Directory.CreateDirectory(directory);
        if (pending) File.WriteAllText(Path.Combine(directory, ".pending"), "Verifying company copy");
        File.WriteAllText(Path.Combine(directory, "company.json"), JsonSerializer.Serialize(name));
        return new(id, name, directory);
    }
}
