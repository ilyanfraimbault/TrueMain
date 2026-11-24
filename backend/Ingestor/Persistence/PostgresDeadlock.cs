using Npgsql;

namespace Ingestor.Persistence;

public static class PostgresDeadlock
{
    /// <summary>
    /// True when <paramref name="exception"/>, or one of its inner exceptions (EF's execution
    /// strategy wraps it), is Postgres choosing this transaction as a deadlock victim (40P01).
    /// </summary>
    public static bool IsVictim(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}
