using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace PaymentPlatform.Persistence;

/// <summary>SQL Server errors a use case expects and handles, recognized by number.</summary>
public static class DatabaseErrors
{
    /// <summary>A save lost a race to insert the same key: 2627 for a primary key or unique constraint, 2601 for a unique index.</summary>
    public static bool IsDuplicateKey(DbUpdateException exception) =>
        exception?.InnerException is SqlException { Number: 2627 or 2601 };
}
