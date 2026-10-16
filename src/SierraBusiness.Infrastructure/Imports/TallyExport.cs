using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;

public sealed partial class ImportService
{
    private static string TallyParent(LedgerAccount a) => a.Id switch
    {
        1000 => "Cash-in-Hand", 1100 => "Sundry Debtors", 1200 => "Stock-in-Hand", 2000 => "Sundry Creditors", 2100 => "Duties & Taxes", 4000 => "Sales Accounts", 5000 => "Purchase Accounts",
        _ => a.Group switch { LedgerGroup.Asset => a.CashOrBank ? "Bank Accounts" : "Current Assets", LedgerGroup.Liability => "Current Liabilities", LedgerGroup.Equity => "Capital Account", LedgerGroup.Income => "Indirect Incomes", _ => "Indirect Expenses" }
    };

    public async Task<byte[]> ExportTally(DateOnly from, DateOnly to)
    {
        var actor = await Authorize(); actor.Demand("accounting.read");
        if (from > to || from.Year < 2000 || to.DayNumber - from.DayNumber > 3660) throw new BusinessException("Choose an export period of at most ten years, starting in 2000 or later.");
        await using var db = await factory.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var accounts = await db.LedgerAccounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(); var names = accounts.ToDictionary(a => a.Id,a => a.Name);
        var documents = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate >= from && d.BusinessDate <= to).OrderBy(d => d.BusinessDate).ThenBy(d => d.Number).ToListAsync();
        if (documents.Count > 50000) throw new BusinessException("Export at most 50,000 documents at a time; choose a shorter period.");
        var ids = documents.Select(d => d.Id).ToArray();
        var lines = (await db.Journal.AsNoTracking().Where(j => ids.Contains(j.DocumentId)).ToListAsync()).GroupBy(j => j.DocumentId).ToDictionary(g => g.Key,g => g.ToList());
        await transaction.CommitAsync();
        static string Amount(decimal value) => value.ToString("0.00",CultureInfo.InvariantCulture);
        var messages = new List<XElement>();
        messages.AddRange(accounts.Select(a => new XElement("TALLYMESSAGE",new XElement("LEDGER",new XAttribute("NAME",a.Name),new XAttribute("ACTION","Create"),
            new XElement("NAME.LIST",new XElement("NAME",a.Name)),new XElement("PARENT",TallyParent(a)),new XElement("ISBILLWISEON",a.Id is 1100 or 2000 ? "Yes" : "No"),new XElement("UDF_ROKELACODE",a.Id.ToString(CultureInfo.InvariantCulture))))));
        foreach (var doc in documents)
        {
            if (!lines.TryGetValue(doc.Id,out var entries) || entries.Count == 0) continue;
            var type = doc.Kind switch { DocumentKind.Payment => "Payment", DocumentKind.MoneyReceipt => "Receipt", DocumentKind.Contra => "Contra", _ => "Journal" };
            // Tally convention: debits are deemed positive and carry negative amounts; credits are positive.
            messages.Add(new XElement("TALLYMESSAGE",new XElement("VOUCHER",new XAttribute("VCHTYPE",type),new XAttribute("ACTION","Create"),
                new XElement("DATE",doc.BusinessDate.ToString("yyyyMMdd",CultureInfo.InvariantCulture)),new XElement("VOUCHERTYPENAME",type),new XElement("VOUCHERNUMBER",doc.Number),
                new XElement("PARTYLEDGERNAME",doc.Party),new XElement("NARRATION",$"{doc.Kind}: {doc.Party} {doc.Settlement}".Trim()),
                entries.GroupBy(e => (int)e.Account).Select(g => g.Sum(e => e.Debit-e.Credit)).Zip(entries.GroupBy(e => (int)e.Account).Select(g => g.Key)).Where(x => x.First != 0).Select(x => new XElement("ALLLEDGERENTRIES.LIST",
                    new XElement("LEDGERNAME",names.GetValueOrDefault(x.Second,x.Second.ToString(CultureInfo.InvariantCulture))),new XElement("ISDEEMEDPOSITIVE",x.First > 0 ? "Yes" : "No"),new XElement("AMOUNT",Amount(-x.First)))))));
        }
        var xml = new XDocument(new XDeclaration("1.0","utf-8",null),new XElement("ENVELOPE",new XElement("HEADER",new XElement("TALLYREQUEST","Import Data")),
            new XElement("BODY",new XElement("IMPORTDATA",new XElement("REQUESTDESC",new XElement("REPORTNAME","All Masters"),new XElement("STATICVARIABLES",new XElement("SVFROMDATE",from.ToString("yyyyMMdd",CultureInfo.InvariantCulture)),new XElement("SVTODATE",to.ToString("yyyyMMdd",CultureInfo.InvariantCulture)))),
                new XElement("REQUESTDATA",messages)))));
        using var stream = new MemoryStream(); using (var writer = XmlWriter.Create(stream,new XmlWriterSettings { Indent = true,Encoding = new System.Text.UTF8Encoding(false) })) xml.Save(writer);
        return stream.ToArray();
    }
}
