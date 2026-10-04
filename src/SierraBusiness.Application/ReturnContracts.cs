namespace SierraBusiness.Application;
public sealed record ReturnLine(Guid ProductId, string? Serial, string? Batch, DateOnly? Expiry, decimal Quantity);
public enum ReturnDisposition { Original, CashRefund, BankRefund, CustomerCredit, SupplierCredit }
public sealed record ReturnCommand(Guid OperationId, Guid OriginalDocumentId, string Reason, List<ReturnLine> Lines, ReturnDisposition Disposition = ReturnDisposition.Original, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] Guid? ApprovalId = null);
public sealed record ReturnableLine(Guid ProductId, string Name, string? Serial, string? Batch, DateOnly? Expiry, decimal OriginalQuantity, decimal ReturnedQuantity);
public sealed record ReturnableDocument(Guid Id, string Number, string Party, bool Sale, string Location, IReadOnlyList<ReturnableLine> Lines);
public interface IReturnService { Task<IReadOnlyList<ReturnableDocument>> Read(); Task<Guid> Post(ReturnCommand input); }

