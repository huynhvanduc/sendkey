using System;

namespace BigSample;

// File .cs thứ hai — dùng để test cột "csharpFile" trong mapping.csv
// (các dòng mapping trỏ tới file này thay vì Program.cs).
static class Steps
{
#pragma warning disable CS0164
    public static decimal Recalc(decimal input, out int rc)
    {
        decimal amount = input;

    RECALC:
        amount = Math.Round(input * 1.03m, 2);
        rc = amount >= 0m ? 0 : 40;
        Console.WriteLine($"RECALC: amount={amount}, rc={rc}");
        return amount;
    }

    public static void AuditLog(string msg)
    {
        string message = msg;

    AUDIT_LOG:
        message = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        Console.WriteLine($"AUDIT_LOG: {message}");
    }

    public static int Rollback(int inRc)
    {
        int rc = inRc;

    ROLLBACK:
        rc = 0;
        Console.WriteLine($"ROLLBACK: rc={rc}");
        return rc;
    }
#pragma warning restore CS0164
}
