using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public sealed partial class ComplianceService
{
    public async Task<IReadOnlyList<InputTaxSource>> InputTaxSources()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync();
        var documents = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && (d.Kind == DocumentKind.Payment || d.Kind == DocumentKind.Receipt) && !db.Documents.Any(r => r.Reverses == d.Id)).OrderByDescending(d => d.BusinessDate).Take(5000).ToListAsync();
        return documents.Select(d => new InputTaxSource(d.Id,d.Number,d.BusinessDate,d.Party,d.Kind == DocumentKind.Receipt,d.Total)).ToArray();
    }
    public async Task<Guid> RecordInputTax(InputTaxCommand input)
    {
        var actor = await Authorize("accounting.read", "accounting.write");
        return await Commands.Run(factory, actor, input.OperationId, "tax.input.claim", input, async db =>
        {
            if (input.Date.Year < 2000 || input.Date > Today || input.Date <= await PeriodControls.ClosedThrough(db, actor.Branch, options.Value.ClosedThrough)) throw new BusinessException("Use a current or past open-period claim date.");
            if (Controls.Backdated(await Controls.Current(db, actor.Branch), input.Date) is not null) throw new BusinessException("This claim date requires backdating approval. Record the claim on an allowed current date after reviewing its source invoice.");
            Rules.Required(input.Supplier, "Supplier", 160); Rules.Required(input.SupplierTin, "Supplier TIN or customs identifier", 80); Rules.Required(input.InvoiceReference, "Tax invoice or customs reference", 80); Rules.Required(input.ReviewReason, "Allowability review reason", 500);
            foreach (var amount in new[] { input.NetPurchase, input.TaxPaid, input.AllowableTax }) if (amount < 0 || amount > 1_000_000_000 || AccountingEngine.Money(amount) != amount) throw new BusinessException("Tax amounts must be non-negative, at most one billion and have two decimal places.");
            if (input.NetPurchase <= 0 || input.TaxPaid <= 0 || input.AllowableTax <= 0 || input.AllowableTax > input.TaxPaid) throw new BusinessException("Enter positive purchase/tax amounts and an allowable credit no larger than tax paid.");
            if (!(await TaxRules.Profile(db, actor.Branch)).TaxRegistered) throw new BusinessException("Input-tax recovery requires a tax-registered company profile.");
            var payment = await db.Documents.SingleOrDefaultAsync(d => d.Id == input.PaymentDocumentId && d.Branch == actor.Branch && d.Kind == (input.Stock ? DocumentKind.Receipt : DocumentKind.Payment) && !db.Documents.Any(r => r.Reverses == d.Id)) ?? throw new BusinessException("Choose an active source expense payment or direct goods receipt containing this tax invoice.");
            if (payment.BusinessDate > input.Date) throw new BusinessException("The claim cannot precede its expense payment.");
            var expense = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.ExpenseAccount);
            var recovery = await db.LedgerAccounts.SingleOrDefaultAsync(a => a.Id == input.RecoveryAccount);
            if ((input.Stock ? input.ExpenseAccount != 1200 : expense?.Group != LedgerGroup.Expense || expense.Control) || recovery?.Group != LedgerGroup.Asset || recovery.Control || recovery.CashOrBank) throw new BusinessException("Stock claims credit inventory 1200; expense claims use a custom expense. Choose a separate non-bank custom recovery asset.");
            if (!input.Stock)
            {
                var expenseAmount = (await db.Journal.Where(j => j.DocumentId == payment.Id && (int)j.Account == input.ExpenseAccount).ToListAsync()).Sum(j => j.Debit - j.Credit);
                if (expenseAmount != input.NetPurchase + input.TaxPaid) throw new BusinessException("The source expense line must equal this invoice's purchase value plus tax. Split combined invoices into separate source payments before claiming.");
            }
            var stockChanges = new List<Movement>(); decimal inventoryCredit = 0, costOfSalesCredit = 0, varianceCredit = 0;
            if (input.Stock)
            {
                if (!payment.Party.Equals(input.Supplier.Trim(),StringComparison.OrdinalIgnoreCase)) throw new BusinessException("The reviewed supplier must match the source goods receipt.");
                if (Grni.IsAwaiting(payment)) throw new BusinessException("Match the supplier invoice before reviewing input tax; this direct-receipt workflow cannot claim from uninvoiced goods.");
                if (await db.ReturnAllocations.AnyAsync(a => a.OriginalDocumentId == payment.Id && !db.Documents.Any(r => r.Reverses == a.ReturnDocumentId))) throw new BusinessException("This receipt has an active purchase return. Claim GST on a receipt only before goods go back to the supplier, or after reversing the return.");
                var sourceRows = await db.Movements.AsNoTracking().Where(m => m.DocumentId == payment.Id && m.Quantity > 0).ToListAsync();
                var policies = new Dictionary<Guid, InventoryCostMethod>();
                foreach (var product in sourceRows.Select(m => m.ProductId).Distinct()) { policies[product] = (await InventoryCost.Policy(db,actor.Branch,product)).Method; await InventoryCost.GuardDate(db,actor.Branch,product,input.Date); }
                var averaged = sourceRows.Where(m => policies[m.ProductId] != InventoryCostMethod.Standard).ToList(); var standard = sourceRows.Count != averaged.Count;
                // Standard-cost lines carry stock at standard; their gross is what the payable exceeds the other lines by.
                if (sourceRows.Count == 0 || payment.Total != input.NetPurchase+input.TaxPaid || (standard ? averaged.Sum(m => m.Value) >= payment.Total : averaged.Sum(m => m.Value) != payment.Total)) throw new BusinessException("Use a direct receipt whose recorded gross stock value equals the reviewed invoice net plus GST.");
                // Allowable GST follows each line's share of the gross invoice; a standard-cost share reverses part of the purchase-price variance (5100).
                decimal allocated = 0, receivedValue = 0; var shares = new Dictionary<Guid, decimal>();
                for (var i = 0; i < averaged.Count; i++)
                {
                    receivedValue += averaged[i].Value; var cumulative = i == averaged.Count-1 && !standard ? input.AllowableTax : AccountingEngine.Money(input.AllowableTax*receivedValue/payment.Total);
                    shares[averaged[i].ProductId] = shares.GetValueOrDefault(averaged[i].ProductId)+cumulative-allocated; allocated = cumulative;
                }
                varianceCredit = input.AllowableTax-allocated;
                // Recovered GST on goods still held reduces their carrying value; GST on goods already issued reduces cost of sales.
                var current = await db.Movements.AsNoTracking().Where(m => m.Branch == actor.Branch && shares.Keys.Contains(m.ProductId)).ToListAsync();
                foreach (var (product, share) in shares)
                {
                    if (share == 0) continue;
                    var received = averaged.Where(m => m.ProductId == product).Sum(m => m.Quantity); var stock = current.Where(m => m.ProductId == product).ToList();
                    var held = Math.Min(Math.Max(stock.Sum(m => m.Quantity),0),received);
                    var identities = stock.GroupBy(m => new InventoryCost.Identity(m.ProductId,m.Location,m.Serial,m.Batch,m.Expiry)).Select(g => (g.Key, Quantity: g.Sum(m => m.Quantity), Value: g.Sum(m => m.Value))).Where(g => g.Quantity > 0 && g.Value > 0).ToList();
                    var own = identities.Where(g => averaged.Any(m => m.ProductId == product && m.Location == g.Key.Location && m.Serial == g.Key.Serial && m.Batch == g.Key.Batch && m.Expiry == g.Key.Expiry)).ToList(); if (own.Count > 0) identities = own;
                    var inventoryPart = Math.Min(held == received ? share : AccountingEngine.Money(share*held/received),identities.Sum(g => g.Value));
                    decimal spread = 0;
                    for (var i = 0; i < identities.Count && inventoryPart > 0; i++)
                    {
                        var g = identities[i]; var value = i == identities.Count-1 ? inventoryPart-spread : Math.Min(g.Value,AccountingEngine.Money(inventoryPart*g.Value/identities.Sum(x => x.Value))); spread += value; if (value == 0) continue;
                        stockChanges.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,ProductId = product,Location = g.Key.Location,Serial = g.Key.Serial,Batch = g.Key.Batch,Expiry = g.Key.Expiry,Quantity = 0,Value = -value });
                    }
                    inventoryCredit += inventoryPart; costOfSalesCredit += share-inventoryPart;
                }
            }
            var claims = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.input.claim" && !db.Documents.Any(r => r.Reverses == o.ResultId)).ToListAsync()).Select(o => JsonSerializer.Deserialize<InputTaxCommand>(o.Payload)!).ToArray();
            if (claims.Any(c => c.PaymentDocumentId == payment.Id || c.SupplierTin.Trim().Equals(input.SupplierTin.Trim(), StringComparison.OrdinalIgnoreCase) && c.InvoiceReference.Trim().Equals(input.InvoiceReference.Trim(), StringComparison.OrdinalIgnoreCase))) throw new BusinessException("This source payment or supplier invoice already has an active input-tax claim.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-IT-{input.OperationId:N}", Kind = DocumentKind.Journal, BusinessDate = input.Date, Party = input.Supplier.Trim(), Total = input.AllowableTax, Settlement = "Input tax recovery", Detail = JsonSerializer.Serialize(input) };
            db.Documents.Add(doc);
            foreach (var movement in stockChanges) { movement.DocumentId = doc.Id; db.Movements.Add(movement); }
            db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = (Account)input.RecoveryAccount, Debit = input.AllowableTax });
            if (!input.Stock) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = (Account)input.ExpenseAccount, Credit = input.AllowableTax });
            else foreach (var (account, amount) in new[] { (Account.Inventory, inventoryCredit), (Account.CostOfSales, costOfSalesCredit), ((Account)5100, varianceCredit) }) if (amount != 0) db.Journal.Add(new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = account, Credit = amount });
            return doc.Id;
        });
    }
    public async Task<NetTaxReturnReport> NetTaxReturn(DateOnly from, DateOnly to)
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable); var result = await NetTaxReturnFor(db,actor,from,to); await transaction.CommitAsync(); return result;
    }
    private static async Task<NetTaxReturnReport> NetTaxReturnFor(BusinessDb db,Actor actor,DateOnly from,DateOnly to)
    {
        var output = await TaxReturnFor(db,actor,from,to);
        var docs = await db.Documents.AsNoTracking().Where(d => d.Branch == actor.Branch && d.BusinessDate >= from && d.BusinessDate <= to && (d.Settlement == "Input tax recovery" && d.Reverses == null || d.Kind == DocumentKind.Reversal && db.Documents.Any(p => p.Id == d.Reverses && p.Settlement == "Input tax recovery"))).ToListAsync(); var ids = docs.Where(d => d.Reverses != null).Select(d => d.Reverses!.Value).ToArray(); var parents = await db.Documents.Where(d => ids.Contains(d.Id) && d.Branch == actor.Branch).ToDictionaryAsync(d => d.Id);
        var inputs = docs.Select(d => new InputTaxView(d.Id,JsonSerializer.Deserialize<InputTaxCommand>(d.Reverses is Guid p ? parents[p].Detail : d.Detail)!,d.Number,d.Reverses is null ? 1 : -1,d.BusinessDate)).ToArray();
        return new(output,inputs,inputs.Where(i => !i.Claim.Imported).Sum(i => i.Sign*i.Claim.NetPurchase),inputs.Where(i => !i.Claim.Imported).Sum(i => i.Sign*i.Claim.TaxPaid),inputs.Where(i => i.Claim.Imported).Sum(i => i.Sign*i.Claim.NetPurchase),inputs.Where(i => i.Claim.Imported).Sum(i => i.Sign*i.Claim.TaxPaid),inputs.Sum(i => i.Sign*i.Claim.AllowableTax));
    }
    public async Task<byte[]> ExportNetTaxReturn(DateOnly from, DateOnly to)
    {
        var report = await NetTaxReturn(from, to); var net = report.Output.Lines.Last(); string M(decimal amount) => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        return Imports.WorkbookWriter.Write([("GST review", [["GST working paper", from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), "NLe; review before filing; no authority submission"], ["Field", "Amount"], ["Standard-rated net supplies", M(net.Standard)], ["Output GST", M(report.Output.OutputTax)], ["Zero-rated supplies", M(net.ZeroRated)], ["Exempt supplies", M(net.Exempt)], ["Relieved supplies", M(net.Relieved)], ["Local purchases excluding GST", M(report.LocalNet)], ["Local input GST", M(report.LocalTax)], ["Imported purchases excluding GST", M(report.ImportedNet)], ["Imported input GST", M(report.ImportedTax)], ["Allowable input GST", M(report.AllowableInputTax)], ["Net GST payable / credit", M(report.NetPayable)]]), ("Input evidence", [["Claim", "Date", "Supplier", "TIN/customs", "Invoice", "Imported", "Net", "Paid GST", "Allowable", "Reason"], ..report.Inputs.Select(i => (IReadOnlyList<string>)[i.Number, (i.EventDate ?? i.Claim.Date).ToString("yyyy-MM-dd"), i.Claim.Supplier, i.Claim.SupplierTin, i.Claim.InvoiceReference, i.Claim.Imported.ToString(), M(i.Sign*i.Claim.NetPurchase), M(i.Sign*i.Claim.TaxPaid), M(i.Sign*i.Claim.AllowableTax), i.Claim.ReviewReason])])]);
    }
}
