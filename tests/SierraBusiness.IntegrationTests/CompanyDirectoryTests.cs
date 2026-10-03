using SierraBusiness.Desktop;
using SierraBusiness.Domain;
using Xunit;
namespace SierraBusiness.IntegrationTests;
public sealed class CompanyDirectoryTests
{
    [Fact] public void CompaniesHaveSeparateDataAndLegacyDirectoryIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sierra-companies-" + Guid.NewGuid().ToString("N"));
        try
        {
            var companies = new CompanyDirectory(directory); var one = companies.Create("One"); var two = companies.Create("Two");
            Assert.Equal(Path.GetFullPath(directory), companies.Resolve(Guid.Empty).DataDirectory);
            Assert.NotEqual(one.DataDirectory, two.DataDirectory);
            Assert.Equal("One", new CompanyDirectory(directory).Resolve(one.Id).Name);
            Assert.Equal(3, companies.List().Count);
            Assert.Throws<BusinessException>(() => companies.Resolve(Guid.NewGuid()));
            Assert.Throws<BusinessException>(() => companies.Create(" "));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

