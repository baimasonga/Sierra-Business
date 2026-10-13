namespace SierraBusiness.Application;
public sealed record ChequeBookCommand(Guid OperationId, Guid? Id, int ExpectedVersion, int BankAccount, string Name, string Prefix, int First, int Last, int Digits, bool Active);
public sealed record ChequeBookView(Guid Id, int Version, int BankAccount, string Name, string Prefix, int First, int Last, int Digits, bool Active, int Used, string? NextNumber);
public sealed record ChequePoint(decimal X, decimal Y, decimal Width);
public sealed record ChequeLayoutCommand(Guid OperationId, Guid? Id, int ExpectedVersion, int BankAccount, string Name, decimal Width, decimal Height, decimal FontSize, ChequePoint Date, ChequePoint Payee, ChequePoint Amount, ChequePoint Words, ChequePoint Number);
public sealed record ChequeLayoutView(Guid Id, int Version, ChequeLayoutCommand Layout);
public sealed record ChequePrintRequest(Guid LayoutId, IReadOnlyList<Guid> ChequeIds, bool Test,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? ExpectedLayoutVersion = null);
public sealed record ChequePrintPreview(ChequeLayoutView Layout, IReadOnlyList<IssuedChequeView> Cheques, bool Test);
public sealed record ChequePrintCommand(Guid OperationId, ChequePrintRequest Request);
