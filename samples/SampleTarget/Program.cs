using System;
using System.Threading;

namespace SampleTarget;

static class Program
{
#pragma warning disable CS0164 // nhãn không được tham chiếu (mô phỏng cấu trúc batch)
    static void Main()
    {
        int rc = 0;
        string inputFile = "data.txt";
        bool inputExists = true;      // giả lập input hợp lệ để chạy hết các label
        string inDate = "";

        Console.WriteLine("SampleTarget khởi động — F5 trong VS để debug.");

    CHECK_INPUT:
        rc = inputExists ? 0 : 1;
        Console.WriteLine($"CHECK_INPUT: inputFile={inputFile}, rc={rc}");
        if (rc != 0) goto END_PROC;

    VALIDATE_DATE:
        inDate = "2026-09-08";
        rc = inDate.Length == 10 ? 0 : 1;
        Console.WriteLine($"VALIDATE_DATE: inDate={inDate}, rc={rc}");
        goto END_PROC;

    END_PROC:
        Console.WriteLine($"END_PROC: rc={rc}");
        Thread.Sleep(60_000);        // giữ tiến trình sống đủ lâu để thao tác watch / chụp
    }
#pragma warning restore CS0164
}
