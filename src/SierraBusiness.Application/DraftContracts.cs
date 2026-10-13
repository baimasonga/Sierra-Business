namespace SierraBusiness.Application;

public sealed record SaleEntryDraft(Guid ProductId, decimal Quantity, decimal Price, decimal Conversion, string Serial, string Batch, DateOnly? Expiry, string Search, string HoldLabel, Guid? HoldOperationId = null);
public sealed record RecoverableSaleDraft(TradeCommand Document, SaleEntryDraft Entry);
public interface ISaleDraftRecovery
{
    Task<RecoverableSaleDraft?> Read();
    Task Save(RecoverableSaleDraft draft);
    Task Clear();
}

public sealed record DocumentDraft(string Scope, Guid OperationId, string Json);
public interface IDocumentDraftRecovery
{
    Task<DocumentDraft?> Read(string scope);
    Task Save(DocumentDraft draft);
    Task Clear(string scope);
}
