using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;
public sealed partial class ImportService
{
    private static string OpeningBillNumber(string branch,string bill) => $"{branch}-OB-{bill.ToUpperInvariant()}";

    private static async Task CheckOpeningBalances(BusinessDb db,Actor actor,List<Line> lines,DateOnly asAt)
    {
        var accounts = await db.LedgerAccounts.AsNoTracking().ToDictionaryAsync(a => a.Id);
        var parties = await db.Parties.AsNoTracking().Where(p => p.Branch == actor.Branch).ToListAsync();
        var numbers = (await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch).Select(d => d.Number).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var code = Integer(line,"account_code","Account code",true); var debit = ParseNumber(line,"debit","Debit",false,true) ?? 0; var credit = ParseNumber(line,"credit","Credit",false,true) ?? 0;
            line.Values["debit"] = debit.ToString(CultureInfo.InvariantCulture); line.Values["credit"] = credit.ToString(CultureInfo.InvariantCulture);
            if ((debit == 0) == (credit == 0) || AccountingEngine.Money(debit) != debit || AccountingEngine.Money(credit) != credit || debit > 1_000_000_000 || credit > 1_000_000_000) line.Errors.Add("Enter one positive debit or credit of at most one billion, with two decimal places.");
            foreach (var item in new[] { ("party",160),("bill_number",60) }) line.Values[item.Item1] = Text(line,item.Item1,item.Item1,false,item.Item2) ?? "";
            if (!accounts.TryGetValue(code,out var account)) { line.Errors.Add($"Account {code} does not exist. Import ledgers first."); continue; }
            var trade = code is (int)Account.Receivables or (int)Account.Payables;
            if (account.Control && !trade) line.Errors.Add(code == (int)Account.Inventory ? "Bring stock in through the opening stock templates." : $"{account.Name} is a control account posted only by its own workflow.");
            if (!trade) { if (line.V("party").Length > 0 || line.V("bill_number").Length > 0) line.Errors.Add("Party and bill apply only to customer (1100) and supplier (2000) bills."); continue; }
            if (code == (int)Account.Receivables && credit != 0) line.Errors.Add("Customer opening bills are debits. Record customer advances under Deposits & advances.");
            if (code == (int)Account.Payables && debit != 0) line.Errors.Add("Supplier opening bills are credits. Record supplier advances under Deposits & advances.");
            var party = parties.SingleOrDefault(p => p.Name.Equals(line.V("party"),StringComparison.OrdinalIgnoreCase));
            if (party is null) line.Errors.Add("Register the customer or supplier first; use its exact name.");
            else if (code == (int)Account.Receivables ? party.Type == PartyType.Supplier : party.Type == PartyType.Customer) line.Errors.Add($"{party.Name} is not registered as a {(code == (int)Account.Receivables ? "customer" : "supplier")}.");
            else line.Values["party"] = party.Name;
            if (line.V("bill_number").Length == 0) line.Errors.Add("Each customer or supplier opening bill needs its original bill number.");
            else if (numbers.Contains(OpeningBillNumber(actor.Branch,line.V("bill_number")))) line.Errors.Add("This opening bill was already imported.");
            var billDate = ParseDate(line,"bill_date","Bill date") ?? asAt; var due = ParseDate(line,"due_date","Due date") ?? billDate;
            if (billDate > asAt || due < billDate) line.Errors.Add("The bill date must be on or before the opening date and the due date on or after the bill date.");
            line.Values["bill_date"] = billDate.ToString("yyyy-MM-dd"); line.Values["due_date"] = due.ToString("yyyy-MM-dd");
        }
        Duplicates(lines,l => l.V("bill_number").Length == 0 ? null : l.V("bill_number").ToUpperInvariant(),"This bill number");
    }

    private IEnumerable<Guid> PostOpeningBalances(BusinessDb db,Actor actor,ImportBatch batch,List<Line> lines)
    {
        var asAt = batch.AsAt!.Value; var ids = new List<Guid>();
        void Journal(Guid doc,int account,decimal debit,decimal credit) { if (debit != 0 || credit != 0) db.Journal.Add(new() { Id = Guid.NewGuid(),DocumentId = doc,Branch = actor.Branch,Account = (Account)account,Debit = debit,Credit = credit }); }
        foreach (var line in lines.Where(l => l.V("bill_number").Length > 0))
        {
            var customer = line.V("account_code") == "1100"; var amount = customer ? Value(line,"debit") : Value(line,"credit");
            var id = Derived(batch.Key + "|opening-bill|" + line.V("bill_number").ToUpperInvariant());
            db.Documents.Add(new BusinessDocument { Id = id,Number = OpeningBillNumber(actor.Branch,line.V("bill_number")),Branch = actor.Branch,Kind = DocumentKind.OpeningBalance,Party = line.V("party"),BusinessDate = asAt,
                DueDate = DateOnly.ParseExact(line.V("due_date"),"yyyy-MM-dd",CultureInfo.InvariantCulture),Total = amount,Settlement = customer ? "Credit" : "Supplier payable",
                Detail = JsonSerializer.Serialize(new { Import = batch.Id,File = batch.FileName,Bill = line.V("bill_number"),BillDate = line.V("bill_date"),DueDate = line.V("due_date") }) });
            if (customer) { Journal(id,(int)Account.Receivables,amount,0); Journal(id,3200,0,amount); } else { Journal(id,3200,amount,0); Journal(id,(int)Account.Payables,0,amount); }
            db.Operations.Add(Audit(Derived(batch.Key + "|opening-bill-op|" + line.V("bill_number").ToUpperInvariant()),id,actor,"import.opening-bill",new { Batch = batch.Id,Document = id },batch.Key));
            line.EntityId = id; ids.Add(id);
        }
        var ledgers = lines.Where(l => l.V("bill_number").Length == 0).ToList();
        if (ledgers.Count > 0)
        {
            var id = Derived(batch.Key + "|opening-ledgers"); decimal debit = ledgers.Sum(l => Value(l,"debit")),credit = ledgers.Sum(l => Value(l,"credit"));
            db.Documents.Add(new BusinessDocument { Id = id,Number = $"{actor.Branch}-{options.Value.Device}-OB-{id:N}",Branch = actor.Branch,Kind = DocumentKind.OpeningBalance,Party = "Opening balance",BusinessDate = asAt,Total = Math.Max(debit,credit),Settlement = "Opening balances",
                Detail = JsonSerializer.Serialize(new { Import = batch.Id,File = batch.FileName,Lines = ledgers.Select(l => new { Account = l.V("account_code"),Debit = Value(l,"debit"),Credit = Value(l,"credit") }) }) });
            foreach (var group in ledgers.GroupBy(l => int.Parse(l.V("account_code"),CultureInfo.InvariantCulture))) { var net = group.Sum(l => Value(l,"debit")-Value(l,"credit")); Journal(id,group.Key,Math.Max(0,net),Math.Max(0,-net)); }
            Journal(id,3200,Math.Max(0,credit-debit),Math.Max(0,debit-credit));
            db.Operations.Add(Audit(Derived(batch.Key + "|opening-ledgers-op"),id,actor,"import.opening-balances",new { Batch = batch.Id,Document = id },batch.Key));
            foreach (var line in ledgers) line.EntityId = id; ids.Add(id);
        }
        return ids;
    }
}
