namespace SierraBusiness.Application;
public sealed record TaxFilingPreview(NetTaxReturnReport Report,string CalculationFingerprint,CompanyProfile? Company = null);
public sealed record TaxFilingCommand(Guid OperationId,DateOnly From,DateOnly To,string ExpectedCalculation,string FilingReference,string Reason,int BankAccount,int CarryAccount,decimal CarryCreditUsed,bool SourcesReconciled,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] decimal? DeclaredNetTax = null,int RoundingAccount = 5100);
public sealed record TaxFilingView(Guid Id,string Number,DateOnly From,DateOnly To,DateOnly Recorded,string FilingReference,decimal NetTax,decimal CarryUsed,decimal Paid,decimal NewCredit,string By);
/// <summary>A reviewed correction of a recorded GST return, posted in the current open period. The original return stays unchanged.
/// A positive net change (more output or less input tax) is paid or met from carried credit; a negative one becomes carried credit.</summary>
public sealed record TaxFilingCorrectionCommand(Guid OperationId,Guid FilingId,string CorrectionReference,string Reason,decimal OutputTaxChange,decimal InputTaxChange,int AdjustmentAccount,int BankAccount,int CarryAccount,decimal CarryCreditUsed,decimal Penalty = 0,int PenaltyAccount = 0);
public sealed record TaxFilingCorrectionView(Guid Id,string Number,Guid FilingId,DateOnly From,DateOnly To,DateOnly Recorded,string Reference,decimal OutputTaxChange,decimal InputTaxChange,decimal Paid,decimal CarryUsed,decimal NewCredit,decimal Penalty,string By)
{
    public decimal NetChange => OutputTaxChange-InputTaxChange;
}
