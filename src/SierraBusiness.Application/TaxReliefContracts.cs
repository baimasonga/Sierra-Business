namespace SierraBusiness.Application;
public sealed record TaxReliefGrantCommand(Guid OperationId,Guid CustomerId,string AuthorityReference,DateOnly From,DateOnly Through,decimal BranchNetLimit,string ReviewReason);
public sealed record TaxReliefGrantView(Guid Id,Guid CustomerId,string Customer,string AuthorityReference,DateOnly From,DateOnly Through,decimal BranchNetLimit,decimal UsedNet) { public decimal Remaining => BranchNetLimit-UsedNet; }
