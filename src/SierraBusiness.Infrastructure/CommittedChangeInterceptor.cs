using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace SierraBusiness.Infrastructure;

/// <summary>Publish invalidation only when an explicit transaction actually commits.</summary>
internal sealed class CommittedChangeInterceptor : DbTransactionInterceptor
{
    internal static readonly CommittedChangeInterceptor Instance = new();
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => (eventData.Context as BusinessDb)?.NotifyCommitted();
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { (eventData.Context as BusinessDb)?.NotifyCommitted(); return Task.CompletedTask; }
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) { if (eventData.Context is BusinessDb db) db.PendingNotification = false; }
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { if (eventData.Context is BusinessDb db) db.PendingNotification = false; return Task.CompletedTask; }
}
