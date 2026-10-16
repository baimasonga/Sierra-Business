using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure.Imports;

/// <summary>Converts Tally ENVELOPE master exports (GROUP, LEDGER and STOCKITEM) into the review templates. Nothing is posted here:
/// the rows pass the same validation and atomic commit as Excel. Codes Rokela needs but Tally does not have are generated, and
/// values Tally does not hold (such as selling prices) are left empty so validation asks for them rather than inventing them.</summary>
internal static partial class TallyMasters
{
    private static readonly Dictionary<string,(LedgerGroup Group,string Role)> Primary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Current Assets"] = (LedgerGroup.Asset,""),["Fixed Assets"] = (LedgerGroup.Asset,""),["Investments"] = (LedgerGroup.Asset,""),["Deposits (Asset)"] = (LedgerGroup.Asset,""),
        ["Loans & Advances (Asset)"] = (LedgerGroup.Asset,""),["Misc. Expenses (ASSET)"] = (LedgerGroup.Asset,""),["Bank Accounts"] = (LedgerGroup.Asset,"cash"),["Cash-in-Hand"] = (LedgerGroup.Asset,"cash"),
        ["Stock-in-Hand"] = (LedgerGroup.Asset,"stock"),["Sundry Debtors"] = (LedgerGroup.Asset,"customer"),
        ["Current Liabilities"] = (LedgerGroup.Liability,""),["Loans (Liability)"] = (LedgerGroup.Liability,""),["Bank OD A/c"] = (LedgerGroup.Liability,""),["Secured Loans"] = (LedgerGroup.Liability,""),
        ["Unsecured Loans"] = (LedgerGroup.Liability,""),["Duties & Taxes"] = (LedgerGroup.Liability,""),["Provisions"] = (LedgerGroup.Liability,""),["Suspense A/c"] = (LedgerGroup.Liability,""),
        ["Branch / Divisions"] = (LedgerGroup.Liability,""),["Sundry Creditors"] = (LedgerGroup.Liability,"supplier"),
        ["Capital Account"] = (LedgerGroup.Equity,""),["Reserves & Surplus"] = (LedgerGroup.Equity,""),
        ["Sales Accounts"] = (LedgerGroup.Income,""),["Direct Incomes"] = (LedgerGroup.Income,""),["Indirect Incomes"] = (LedgerGroup.Income,""),
        ["Purchase Accounts"] = (LedgerGroup.Expense,""),["Direct Expenses"] = (LedgerGroup.Expense,""),["Indirect Expenses"] = (LedgerGroup.Expense,"")
    };
    private sealed record TallyLedger(string Name,LedgerGroup Group,string Role,XElement Element);

    private static string Name(XElement e) => (e.Attribute("NAME")?.Value ?? e.Element("NAME")?.Value ?? e.Element("NAME.LIST")?.Element("NAME")?.Value ?? "").Trim();
    private static string V(XElement e,string name) => e.Element(name)?.Value.Trim() ?? "";
    private static decimal Number(string text) => decimal.TryParse(Regex.Match(text,@"-?\d[\d,]*(\.\d+)?").Value.Replace(",",""),NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value) ? value : 0;
    private static string Unit(string text) => Regex.Match(text,@"[A-Za-z][A-Za-z.]*\s*$").Value.Trim();
    private static string Money(decimal value) => value.ToString("0.00",CultureInfo.InvariantCulture);
    private static string Date(string tally) => DateOnly.TryParseExact(tally,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d) ? d.ToString("yyyy-MM-dd") : "";
    internal static string Code(string name,int max,HashSet<string> used)
    {
        var baseCode = Regex.Replace(name.ToUpperInvariant(),"[^A-Z0-9]+","-").Trim('-'); if (baseCode.Length == 0) baseCode = "ITEM"; if (baseCode.Length > max) baseCode = baseCode[..max].Trim('-');
        var code = baseCode; for (var i = 2; !used.Add(code); i++) { var suffix = "-" + i; code = (baseCode.Length + suffix.Length > max ? baseCode[..(max-suffix.Length)] : baseCode) + suffix; }
        return code;
    }

    /// <summary>Resolves each ledger's primary group through the file's GROUP hierarchy.</summary>
    private static List<TallyLedger> Ledgers(XDocument xml)
    {
        var parents = xml.Descendants("GROUP").Select(g => (Name: Name(g),Parent: V(g,"PARENT"))).Where(g => g.Name.Length > 0).GroupBy(g => g.Name,StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,g => g.First().Parent,StringComparer.OrdinalIgnoreCase);
        (LedgerGroup,string) Resolve(string group,string ledger)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = group; ; )
            {
                if (Primary.TryGetValue(current,out var primary)) return primary;
                if (!seen.Add(current) || !parents.TryGetValue(current,out var parent) || parent.Length == 0) throw new BusinessException($"Ledger '{ledger}' is under group '{group}', which does not lead to a Tally primary group in this file. Export the groups with the ledgers.");
                current = parent;
            }
        }
        return xml.Descendants("LEDGER").Select(l => (Name: Name(l),Element: l)).Where(l => l.Name.Length > 0 && !l.Name.Equals("Profit & Loss A/c",StringComparison.OrdinalIgnoreCase)).Select(l =>
        {
            var (group,role) = Resolve(V(l.Element,"PARENT"),l.Name); return new TallyLedger(l.Name,group,role,l.Element);
        }).ToList();
    }

    internal static async Task<List<IReadOnlyList<string>>> Rows(BusinessDb db,string branch,XDocument xml,ImportType type,string[] headers)
    {
        var result = new List<IReadOnlyList<string>>();
        void Add(Dictionary<string,string> values) => result.Add(headers.Select(h => values.GetValueOrDefault(h,"")).ToArray());
        switch (type)
        {
            case ImportType.Ledgers:
            {
                var existing = await db.LedgerAccounts.AsNoTracking().ToListAsync(); var names = existing.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var next = Math.Max(6000,existing.Where(a => a.Id >= 6000).Select(a => a.Id + 1).DefaultIfEmpty(6000).Max());
                // Customers and suppliers become party masters; stock comes through stock items; ledgers already present by name are reused.
                foreach (var l in Ledgers(xml).Where(l => l.Role is "" or "cash" && !names.Contains(l.Name)))
                    Add(new() { ["account_code"] = (next++).ToString(CultureInfo.InvariantCulture),["name"] = l.Name,["group"] = l.Group.ToString(),["cash_bank"] = l.Role == "cash" ? "Yes" : "No" });
                break;
            }
            case ImportType.Parties:
            {
                var used = (await db.Parties.AsNoTracking().Where(p => p.Branch == branch).Select(p => p.Code).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var l in Ledgers(xml).Where(l => l.Role is "customer" or "supplier"))
                {
                    var e = l.Element; var address = string.Join(", ",e.Descendants("ADDRESS").Select(a => a.Value.Trim()).Where(a => a.Length > 0));
                    var tin = new[] { "INCOMETAXNUMBER","PARTYGSTIN","VATTINNUMBER","TINNUMBER" }.Select(n => V(e,n)).FirstOrDefault(t => t.Length > 0) ?? "";
                    Add(new() { ["code"] = Code(l.Name,40,used),["name"] = l.Name,["party_type"] = l.Role == "customer" ? "Customer" : "Supplier",["contact"] = V(e,"LEDGERCONTACT"),["phone"] = V(e,"LEDGERMOBILE").Length > 0 ? V(e,"LEDGERMOBILE") : V(e,"LEDGERPHONE"),
                        ["email"] = V(e,"EMAIL"),["address"] = address.Length > 200 ? address[..200] : address,["payment_days"] = V(e,"BILLCREDITPERIOD").Length > 0 ? ((int)Math.Abs(Number(V(e,"BILLCREDITPERIOD")))).ToString(CultureInfo.InvariantCulture) : "",
                        ["credit_limit"] = V(e,"CREDITLIMIT").Length > 0 ? Money(Math.Abs(Number(V(e,"CREDITLIMIT")))) : "",["tin"] = tin });
                }
                break;
            }
            case ImportType.Products or ImportType.OpeningStock:
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in xml.Descendants("STOCKITEM"))
                {
                    var name = Name(item); if (name.Length == 0) continue; var sku = Code(name,40,used);
                    if (type == ImportType.Products)
                    {
                        var parent = V(item,"PARENT"); var rate = item.Descendants("STANDARDPRICELIST.LIST").Select(p => V(p,"RATE")).FirstOrDefault(r => r.Length > 0) ?? "";
                        Add(new() { ["sku"] = sku,["name"] = name.Length > 160 ? name[..160] : name,["category"] = parent.Length == 0 || parent.Equals("Primary",StringComparison.OrdinalIgnoreCase) ? "General" : parent,["unit"] = V(item,"BASEUNITS"),["price"] = rate.Length > 0 ? Money(Number(rate)) : "",["tracking"] = "Quantity" });
                        continue;
                    }
                    var allocations = item.Descendants("BATCHALLOCATIONS.LIST").Where(b => Number(V(b,"OPENINGBALANCE")) != 0).ToList();
                    var places = allocations.Count > 0 ? allocations.Select(b => (Godown: V(b,"GODOWNNAME"),Quantity: Number(V(b,"OPENINGBALANCE")),Value: Number(V(b,"OPENINGVALUE")))).ToList() : [(Godown: "",Quantity: Number(V(item,"OPENINGBALANCE")),Value: Number(V(item,"OPENINGVALUE")))];
                    var itemRate = Number(V(item,"OPENINGRATE"));
                    foreach (var place in places.Where(p => p.Quantity != 0))
                    {
                        var quantity = Math.Abs(place.Quantity); var cost = place.Value != 0 ? Math.Round(Math.Abs(place.Value) / quantity,4) : Math.Abs(itemRate);
                        var godown = place.Godown.Length == 0 || place.Godown.Equals("Main Location",StringComparison.OrdinalIgnoreCase) ? "MAIN" : Code(place.Godown,30,[]);
                        Add(new() { ["sku"] = sku,["warehouse"] = godown,["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),["unit_cost"] = cost.ToString(CultureInfo.InvariantCulture) });
                    }
                }
                break;
            }
            case ImportType.OpeningBalances:
            {
                var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync();
                foreach (var l in Ledgers(xml))
                {
                    // Tally signs opening balances like amounts: negative is a debit.
                    if (l.Role is "customer" or "supplier")
                    {
                        var bills = l.Element.Descendants("BILLALLOCATIONS.LIST").Where(b => Number(V(b,"OPENINGBALANCE")) != 0).ToList();
                        var account = l.Role == "customer" ? "1100" : "2000";
                        if (bills.Count == 0 && Number(V(l.Element,"OPENINGBALANCE")) is var total and not 0) bills = [new XElement("BILL",new XElement("NAME","OPENING-" + Code(l.Name,40,[])),new XElement("OPENINGBALANCE",total.ToString(CultureInfo.InvariantCulture)))];
                        foreach (var bill in bills)
                        {
                            var amount = Number(V(bill,"OPENINGBALANCE")); var billDate = Date(V(bill,"BILLDATE")); var days = Number(V(bill,"BILLCREDITPERIOD"));
                            var due = billDate.Length > 0 && days > 0 ? DateOnly.ParseExact(billDate,"yyyy-MM-dd",CultureInfo.InvariantCulture).AddDays((int)days).ToString("yyyy-MM-dd") : "";
                            Add(new() { ["account_code"] = account,["debit"] = amount < 0 ? Money(-amount) : "",["credit"] = amount > 0 ? Money(amount) : "",["party"] = l.Name,["bill_number"] = Name(bill).Length > 0 ? Name(bill) : V(bill,"NAME"),["bill_date"] = billDate,["due_date"] = due });
                        }
                        continue;
                    }
                    var opening = Number(V(l.Element,"OPENINGBALANCE")); if (opening == 0 || l.Role == "stock") continue;
                    var matching = accounts.Where(a => a.Name.Equals(l.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matching.Length != 1) throw new BusinessException($"Import ledger '{l.Name}' before its opening balance; it must match one account by name.");
                    Add(new() { ["account_code"] = matching[0].Id.ToString(CultureInfo.InvariantCulture),["debit"] = opening < 0 ? Money(-opening) : "",["credit"] = opening > 0 ? Money(opening) : "" });
                }
                break;
            }
            default: throw new BusinessException("Tally XML converts ledgers, customers and suppliers, stock items, opening stock, opening balances and plain accounting vouchers.");
        }
        return result;
    }
}
