using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;
public sealed partial class ImportService
{
    private static async Task CheckStockCostDates(BusinessDb db,Actor actor,List<Line> lines,DateOnly date)
    {
        foreach (var line in lines.Where(l => l.Product is not null && l.Errors.Count == 0))
            try { await InventoryCost.GuardDate(db,actor.Branch,line.Product!.Id,date); if ((await InventoryCost.Policy(db,actor.Branch,line.Product.Id)) is { Method: InventoryCostMethod.Standard } policy && line.Cost != policy.StandardUnitCost) line.Errors.Add("Opening inventory for a standard-cost product must use its configured standard unit cost."); } catch (BusinessException ex) { line.Errors.Add(ex.Message); }
    }
    private static bool Flag(Line line, string key)
    {
        var text = line.V(key).Trim().ToLowerInvariant(); if (text is "" or "no" or "false" or "0") { line.Values[key] = "false"; return false; }
        if (text is "yes" or "true" or "1") { line.Values[key] = "true"; return true; }
        line.Errors.Add($"{key}: use Yes or No."); return false;
    }
    private static int Integer(Line line, string key, string label, bool required, int defaultValue = 0)
    {
        var value = ParseNumber(line,key,label,required,true); if (value is null) return defaultValue;
        if (value.Value != decimal.Truncate(value.Value) || value.Value > int.MaxValue) { line.Errors.Add($"{label} must be a whole number."); return defaultValue; }
        return (int)value.Value;
    }
    private static PartyCommand PartyInput(Line line) => new(Guid.Empty,null,0,line.V("code"),line.V("name"),Enum.Parse<PartyType>(line.V("party_type")),line.V("contact"),line.V("phone"),line.V("email"),line.V("address"),
        int.TryParse(line.V("payment_days"),out var days) ? days : 0, decimal.TryParse(line.V("credit_limit"),NumberStyles.Number,CultureInfo.InvariantCulture,out var limit) ? limit : 0, false,line.V("category"),line.V("tin"));
    private static async Task CheckParties(BusinessDb db, Actor actor, List<Line> lines)
    {
        foreach (var line in lines)
        {
            foreach (var item in new[] { ("code",40,true),("name",160,true),("contact",160,false),("phone",60,false),("email",160,false),("address",200,false),("category",40,false),("tin",30,false) }) line.Values[item.Item1] = Text(line,item.Item1,item.Item1,item.Item3,item.Item2) ?? "";
            line.Values["code"] = line.V("code").ToUpperInvariant();
            if (!Enum.TryParse<PartyType>(line.V("party_type"),true,out var type) || !Enum.IsDefined(type)) line.Errors.Add("Party type must be Customer, Supplier or Both."); else line.Values["party_type"] = type.ToString();
            line.Values["payment_days"] = Integer(line,"payment_days","Payment days",false).ToString(CultureInfo.InvariantCulture);
            var creditLimit = ParseNumber(line,"credit_limit","Credit limit",false,true); if (line.V("credit_limit").Length == 0) line.Values["credit_limit"] = "0";
            if (line.Errors.Count == 0) try { await PartyService.SaveCore(db,actor,PartyInput(line),false); } catch (BusinessException ex) { line.Errors.Add(ex.Message); }
        }
        Duplicates(lines,l => l.V("code"),"Party code"); Duplicates(lines,l => l.V("name"),"Party name");
    }
    private static LedgerInput LedgerInputFor(Line line) => new(Guid.Empty,int.Parse(line.V("account_code"),CultureInfo.InvariantCulture),line.V("name"),Enum.Parse<LedgerGroup>(line.V("group")),line.V("cash_bank") == "true");
    private static async Task CheckLedgers(BusinessDb db, List<Line> lines)
    {
        foreach (var line in lines)
        {
            Integer(line,"account_code","Account code",true); line.Values["name"] = Text(line,"name","Account name",true,160) ?? "";
            if (!Enum.TryParse<LedgerGroup>(line.V("group"),true,out var type) || !Enum.IsDefined(type)) line.Errors.Add("Account type must be Asset, Liability, Equity, Income or Expense."); else line.Values["group"] = type.ToString();
            Flag(line,"cash_bank");
            if (line.Errors.Count == 0) try { await FinanceService.CreateLedgerCore(db,LedgerInputFor(line),false); } catch (BusinessException ex) { line.Errors.Add(ex.Message); }
        }
        Duplicates(lines,l => l.V("account_code"),"Account code");
    }
    private static decimal Value(Line line, string key) => decimal.TryParse(line.V(key),NumberStyles.Number,CultureInfo.InvariantCulture,out var value) ? value : 0;
    private async Task<VoucherCommand> VoucherInput(BusinessDb db, Actor actor, List<Line> lines, Guid operation)
    {
        var first = lines[0]; var allocations = new Dictionary<Guid,decimal>();
        foreach (var line in lines.Where(l => l.V("bill_number").Length > 0))
        {
            var bill = await db.Documents.SingleOrDefaultAsync(d => d.Branch == actor.Branch && d.Number == line.V("bill_number")) ?? throw new BusinessException($"Bill {line.V("bill_number")} was not found in this branch.");
            allocations[bill.Id] = allocations.GetValueOrDefault(bill.Id) + Value(line,"bill_amount");
        }
        return new(operation,Enum.Parse<VoucherType>(first.V("voucher_type")),DateOnly.ParseExact(first.V("date"),"yyyy-MM-dd",CultureInfo.InvariantCulture),first.V("narration"),
            lines.Select(l => new VoucherLine(int.Parse(l.V("account_code"),CultureInfo.InvariantCulture),Value(l,"debit"),Value(l,"credit"),l.V("cost_centre"))).ToList(),allocations.Select(a => new BillSettlement(a.Key,a.Value)).ToList(),ExternalReference:first.V("voucher_key"));
    }
    private async Task CheckVouchers(BusinessDb db, Actor actor, List<Line> lines)
    {
        foreach (var line in lines)
        {
            foreach (var item in new[] { ("voucher_key",80,true),("narration",200,true),("cost_centre",100,false),("bill_number",200,false) }) line.Values[item.Item1] = Text(line,item.Item1,item.Item1,item.Item3,item.Item2) ?? "";
            line.Values["voucher_key"] = line.V("voucher_key").ToUpperInvariant(); Integer(line,"account_code","Account code",true);
            if (ParseDate(line,"date","Voucher date") is null) line.Errors.Add("A voucher date is required.");
            if (!Enum.TryParse<VoucherType>(line.V("voucher_type"),true,out var type) || !Enum.IsDefined(type)) line.Errors.Add("Voucher type must be Journal, Receipt, Payment or Contra."); else line.Values["voucher_type"] = type.ToString();
            ParseNumber(line,"debit","Debit",false,true); ParseNumber(line,"credit","Credit",false,true); if (line.V("debit").Length == 0) line.Values["debit"] = "0"; if (line.V("credit").Length == 0) line.Values["credit"] = "0";
            ParseNumber(line,"bill_amount","Bill allocation amount",line.V("bill_number").Length > 0);
            if (line.V("bill_number").Length == 0 && Value(line,"bill_amount") != 0) line.Errors.Add("A bill number is required for a bill allocation.");
        }
        var finance = new FinanceService(factory,session,options);
        foreach (var group in lines.GroupBy(l => l.V("voucher_key")))
        {
            var rows = group.ToList(); if (rows.Any(l => l.Errors.Count > 0)) continue;
            if (rows.Select(l => (l.V("date"),l.V("voucher_type"),l.V("narration"))).Distinct().Count() != 1) { foreach (var line in rows) line.Errors.Add("Every line of one voucher must share its date, type and narration."); continue; }
            var before = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
            try { var command = await VoucherInput(db,actor,rows,Guid.NewGuid()); await finance.PostVoucherCore(db,actor,command); }
            catch (Controls.ApprovalNeeded) { foreach (var line in rows) line.Errors.Add("This voucher requires approval. Post it through Accounting and its approval workflow instead."); }
            catch (BusinessException ex) { foreach (var line in rows) line.Errors.Add(ex.Message); }
            finally { foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added && !before.Contains(e.Entity)).ToArray()) entry.State = EntityState.Detached; }
        }
    }
}
