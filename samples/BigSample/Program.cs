namespace BigSample;

// Mô phỏng một job batch đã migrate sang C#: giữ nguyên cấu trúc "nhãn:" + goto,
// biến cục bộ, để test mapping mode của SendKeyDemo với nhiều label / nhiều file.
static class Program
{
#pragma warning disable CS0164 // một số nhãn không được goto trực tiếp (mô phỏng batch)
    static void Main()
    {
        int rc = 0;
        string paramFile = "params.dat";
        string inputFile = "orders.csv";
        bool headerOk = false;
        int lineCount = 0;
        decimal tax = 0m;
        decimal total = 0m;
        decimal discount = 0m;
        string outFile = "result.txt";

        Console.WriteLine("BigSample bắt đầu — F5 trong VS để debug.");

    INIT:
        rc = 0;
        Console.WriteLine($"INIT: rc={rc}");

    READ_PARAMS:
        paramFile = Environment.GetEnvironmentVariable("PARAM_FILE") ?? "params.dat";
        rc = paramFile.Length > 0 ? 0 : 8;
        Console.WriteLine($"READ_PARAMS: paramFile={paramFile}, rc={rc}");
        if (rc != 0) goto HANDLE_ERROR;

        CHECK_INPUT:
        inputFile = "orders.csv";
        rc = inputFile.EndsWith(".csv") ? 0 : 12;
        Console.WriteLine($"CHECK_INPUT: inputFile={inputFile}, rc={rc}");
        if (rc != 0) goto HANDLE_ERROR;

        VALIDATE_HEADER:
        headerOk = inputFile.Length > 3;
        rc = headerOk ? 0 : 20;
        Console.WriteLine($"VALIDATE_HEADER: headerOk={headerOk}, rc={rc}");
        if (rc != 0) goto HANDLE_ERROR;

        VALIDATE_DETAIL:
        lineCount = 42;
        rc = lineCount > 0 ? 0 : 24;
        Console.WriteLine($"VALIDATE_DETAIL: lineCount={lineCount}, rc={rc}");
        if (rc != 0) goto HANDLE_ERROR;

        CALC_TAX:
        tax = lineCount * 1.5m;
        Console.WriteLine($"CALC_TAX: tax={tax}");

    CALC_TOTAL:
        total = lineCount * 10m + tax;
        Console.WriteLine($"CALC_TOTAL: total={total}");

    APPLY_DISCOUNT:
        discount = total > 100m ? total * 0.1m : 0m;
        total -= discount;
        Console.WriteLine($"APPLY_DISCOUNT: discount={discount}, total={total}");

    RECALC_VIA_HELPER:
        total = Steps.Recalc(total, out rc);
        Console.WriteLine($"RECALC_VIA_HELPER: total={total}, rc={rc}");
        if (rc != 0) goto ROLLBACK_VIA_HELPER;

        WRITE_OUTPUT:
        outFile = "result.txt";
        rc = outFile.Length > 0 ? 0 : 28;
        Console.WriteLine($"WRITE_OUTPUT: outFile={outFile}, rc={rc}");
        goto CLEANUP;

    ROLLBACK_VIA_HELPER:
        rc = Steps.Rollback(rc);
        Console.WriteLine($"ROLLBACK_VIA_HELPER: rc={rc}");
        goto CLEANUP;

    HANDLE_ERROR:
        rc = rc == 0 ? 99 : rc;
        Steps.AuditLog($"lỗi rc={rc}");
        Console.WriteLine($"HANDLE_ERROR: rc={rc}");

    CLEANUP:
        rc = 0;
        Console.WriteLine($"CLEANUP: rc={rc}");

    END_PROC:
        Console.WriteLine($"END_PROC: rc={rc}, total={total}");
        Thread.Sleep(120_000); // giữ tiến trình sống đủ lâu để thao tác watch / chụp
    }
#pragma warning restore CS0164
}
