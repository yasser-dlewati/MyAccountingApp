namespace MyAccountingApp.Core.Entities;

/// <summary>CreatedAt is stamped (UTC) by AppDbContext.SaveChanges.</summary>
public interface IHasCreatedAt
{
    DateTime CreatedAt { get; set; }
}
