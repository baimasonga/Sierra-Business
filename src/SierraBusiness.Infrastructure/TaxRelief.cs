using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SierraBusiness.Application;
using SierraBusiness.Domain;
namespace SierraBusiness.Infrastructure;

public static partial class TaxRules
{
    internal static async Task<decimal> ReliefUsed(BusinessDb db,string branch,Guid grant)
    {
        var text = grant.ToString(); var invoices = await db.Documents.AsNoTracking().Where(d => d.Branch == branch && (d.Kind == DocumentKind.Sale || d.Kind == DocumentKind.ServiceInvoice) && d.Detail.Contains(text) && !db.Documents.Any(r => r.Reverses == d.Id)).ToListAsync();
        var matched = invoices.Where(d => { using var json = JsonDocument.Parse(d.Detail); return json.RootElement.TryGetProperty("ReliefGrantId",out var id) && id.ValueKind == JsonValueKind.String && id.GetGuid() == grant; }).ToArray(); var ids = matched.Select(d => d.Id).ToArray();
        var returns = await db.ReturnAllocations.AsNoTracking().Where(r => ids.Contains(r.OriginalDocumentId) && !db.Documents.Any(d => d.Reverses == r.ReturnDocumentId)).Select(r => r.ReturnDocumentId).Distinct().ToArrayAsync(); var returned = await db.Documents.AsNoTracking().Where(d => returns.Contains(d.Id)).ToListAsync();
        decimal Amount(BusinessDocument doc) { using var json = JsonDocument.Parse(doc.Detail); return Value(json.RootElement,"Relieved"); }
        return Math.Max(0,matched.Sum(Amount)-returned.Sum(Amount));
    }
    internal static async Task<TaxReliefGrantCommand> ValidateRelief(BusinessDb db,Actor actor,Guid grantId,Guid? customer,DateOnly date,decimal net)
    {
        actor.Demand("accounting.write");
        var operation = await db.Operations.AsNoTracking().SingleOrDefaultAsync(o => o.Branch == actor.Branch && o.Action == "tax.relief-grant" && o.ResultId == grantId) ?? throw new BusinessException("Relief certificate not found in this branch."); var grant = JsonSerializer.Deserialize<TaxReliefGrantCommand>(operation.Payload)!;
        if (customer != grant.CustomerId || date < grant.From || date > grant.Through || !(await Profile(db,actor.Branch)).TaxRegistered) throw new BusinessException("The registered recipient, certificate dates or company GST registration does not match this invoice.");
        if (net <= 0 || net+await ReliefUsed(db,actor.Branch,grantId) > grant.BranchNetLimit) throw new BusinessException("This invoice has no eligible standard-rated supplies or exceeds the certificate's remaining branch-approved net ceiling. Split or review the invoice.");
        return grant;
    }
}
public sealed partial class ComplianceService
{
    public async Task<Guid> RecordReliefGrant(TaxReliefGrantCommand input)
    {
        var actor = await Authorize("accounting.write");
        return await Commands.Run(factory,actor,input.OperationId,"tax.relief-grant",input,async db =>
        {
            Rules.Required(input.AuthorityReference,"Relief certificate reference",80); Rules.Required(input.ReviewReason,"Certificate and branch-allocation review reason",200);
            if (input.From.Year < 2000 || input.From.Year > Today.Year+10 || input.Through < input.From || input.Through > input.From.AddYears(10) || input.BranchNetLimit <= 0 || input.BranchNetLimit > 1_000_000_000 || AccountingEngine.Money(input.BranchNetLimit) != input.BranchNetLimit) throw new BusinessException("Use a valid certificate period of at most ten years and a positive branch-approved net ceiling with two decimal places.");
            if (!await db.Parties.AnyAsync(p => p.Id == input.CustomerId && p.Branch == actor.Branch && p.Type != PartyType.Supplier)) throw new BusinessException("Choose a registered recipient customer.");
            var previous = await db.Operations.Where(o => o.Branch == actor.Branch && o.Action == "tax.relief-grant").ToListAsync(); if (previous.Any(o => JsonSerializer.Deserialize<TaxReliefGrantCommand>(o.Payload)!.AuthorityReference.Trim().Equals(input.AuthorityReference.Trim(),StringComparison.OrdinalIgnoreCase))) throw new BusinessException("This certificate reference is already recorded. Use a separately reviewed amendment reference for changed authority.");
            return Guid.NewGuid();
        });
    }
    public async Task<IReadOnlyList<TaxReliefGrantView>> ReliefGrants()
    {
        var actor = await Authorize("accounting.read"); await using var db = await factory.CreateDbContextAsync(); var operations = await db.Operations.AsNoTracking().Where(o => o.Branch == actor.Branch && o.Action == "tax.relief-grant").ToListAsync(); var parties = await db.Parties.Where(p => p.Branch == actor.Branch).ToDictionaryAsync(p => p.Id,p => p.Name); var result = new List<TaxReliefGrantView>();
        foreach (var operation in operations) { var c = JsonSerializer.Deserialize<TaxReliefGrantCommand>(operation.Payload)!; result.Add(new(operation.ResultId,c.CustomerId,parties[c.CustomerId],c.AuthorityReference,c.From,c.Through,c.BranchNetLimit,await TaxRules.ReliefUsed(db,actor.Branch,operation.ResultId))); } return result;
    }
}
