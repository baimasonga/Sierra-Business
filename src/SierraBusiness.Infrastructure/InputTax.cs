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
            var expenseAmount = (await db.Journal.Where(j => j.DocumentId == payment.Id && (int)j.Account == input.ExpenseAccount).ToListAsync()).Sum(j => j.Debit - j.Credit);
            if (expenseAmount != input.NetPurchase + input.TaxPaid) throw new BusinessException("The source expense line must equal this invoice's purchase value plus tax. Split combined invoices into separate source payments before claiming.");
            var stockChanges = new List<Movement>();
            if (input.Stock)
            {
                if (!payment.Party.Equals(input.Supplier.Trim(),StringComparison.OrdinalIgnoreCase)) throw new BusinessException("The reviewed supplier must match the source goods receipt.");
                if (Grni.IsAwaiting(payment)) throw new BusinessException("Match the supplier invoice before reviewing input tax; this direct-receipt workflow cannot claim from uninvoiced goods.");
                var sourceRows = await db.Movements.AsNoTracking().Where(m => m.DocumentId == payment.Id && m.Quantity > 0).ToListAsync(); if (sourceRows.Count == 0 || sourceRows.Sum(m => m.Value) != payment.Total || payment.Total != input.NetPurchase+input.TaxPaid) throw new BusinessException("Use a direct receipt whose recorded gross stock value equals the reviewed invoice net plus GST.");
                var posted = await db.Operations.Where(o => o.ResultId == payment.Id).Select(o => (DateTimeOffset?)o.Time).FirstOrDefaultAsync() ?? throw new BusinessException("Source receipt posting history not found."); var later = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch).Select(o => new { o.ResultId,o.Time }).ToListAsync()).Where(o => o.Time > posted).Select(o => o.ResultId).ToArray(); var productIds = sourceRows.Select(m => m.ProductId).Distinct().ToArray();
                if (await db.Movements.AnyAsync(m => m.Branch == actor.Branch && productIds.Contains(m.ProductId) && later.Contains(m.DocumentId) && (m.Quantity < 0 || m.Value < 0) && !db.Documents.Any(r => r.Reverses == m.DocumentId))) throw new BusinessException("Recover included purchase GST before goods are issued, transferred, returned or written down. Later cost corrections require an accountant's allocation review.");
                decimal allocated = 0,receivedValue = 0;
                for (var i = 0; i < sourceRows.Count; i++)
                {
                    var row = sourceRows[i]; if ((await InventoryCost.Policy(db,actor.Branch,row.ProductId)).Method == InventoryCostMethod.Standard) throw new BusinessException("Standard-cost tax recovery needs its purchase-variance policy; this stock claim supports average/FIFO receipts."); await InventoryCost.GuardDate(db,actor.Branch,row.ProductId,input.Date);
                    receivedValue += row.Value; var cumulative = i == sourceRows.Count-1 ? input.AllowableTax : AccountingEngine.Money(input.AllowableTax*receivedValue/payment.Total); var value = cumulative-allocated; allocated = cumulative; if (value == 0) continue;
                    stockChanges.Add(new() { Id = Guid.NewGuid(),Branch = actor.Branch,ProductId = row.ProductId,Location = row.Location,Serial = row.Serial,Batch = row.Batch,Expiry = row.Expiry,Quantity = 0,Value = -value });
                }
            }
            var claims = (await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.input.claim" && !db.Documents.Any(r => r.Reverses == o.ResultId)).ToListAsync()).Select(o => JsonSerializer.Deserialize<InputTaxCommand>(o.Payload)!).ToArray();
            if (claims.Any(c => c.PaymentDocumentId == payment.Id || c.SupplierTin.Trim().Equals(input.SupplierTin.Trim(), StringComparison.OrdinalIgnoreCase) && c.InvoiceReference.Trim().Equals(input.InvoiceReference.Trim(), StringComparison.OrdinalIgnoreCase))) throw new BusinessException("This source payment or supplier invoice already has an active input-tax claim.");
            var doc = new BusinessDocument { Id = Guid.NewGuid(), Branch = actor.Branch, Number = $"{actor.Branch}-{options.Value.Device}-IT-{input.OperationId:N}", Kind = DocumentKind.Journal, BusinessDate = input.Date, Party = input.Supplier.Trim(), Total = input.AllowableTax, Settlement = "Input tax recovery", Detail = JsonSerializer.Serialize(input) };
            db.Documents.Add(doc);
            foreach (var movement in stockChanges) { movement.DocumentId = doc.Id; db.Movements.Add(movement); }
            db.Journal.AddRange(new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = (Account)input.RecoveryAccount, Debit = input.AllowableTax }, new JournalLine { Id = Guid.NewGuid(), Branch = actor.Branch, DocumentId = doc.Id, Account = (Account)input.ExpenseAccount, Credit = input.AllowableTax });
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
