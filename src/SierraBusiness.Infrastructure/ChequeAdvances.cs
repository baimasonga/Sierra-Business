using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;
public static class ChequeAdvances
{
    internal static async Task<Dictionary<Guid,ChequeState>> States(BusinessDb db,string branch)
    {
        var cheques = await db.ReceivedCheques.AsNoTracking().Where(c => c.Branch == branch && db.Documents.Any(d => d.Id == c.SourceDocumentId && d.Settlement == "Customer deposit")).ToListAsync(); var ids = cheques.Select(c => c.Id).ToArray();
        var events = await db.ChequeEvents.AsNoTracking().Where(e => ids.Contains(e.ChequeId)).ToListAsync();
        return cheques.ToDictionary(c => c.SourceDocumentId,c => events.Where(e => e.ChequeId == c.Id).MaxBy(e => e.Version)?.State ?? ChequeState.Received);
    }
    internal static async Task DemandAvailable(BusinessDb db,string branch,Guid source)
    {
        if ((await States(db,branch)).TryGetValue(source,out var state) && state != ChequeState.Cleared) throw new BusinessException("A cheque deposit cannot be allocated or refunded until cleared. Bounced deposits have no available credit.");
    }
}
