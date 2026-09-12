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
        rc = Complex.RunAll(rc);
        Console.WriteLine($"END_PROC: sau phần phức tạp, rc={rc}");

        // Giữ tiến trình sống để còn thao tác Watch / chụp. Bấm x để thoát hẳn.
        Console.WriteLine("Xong — bấm x để thoát chương trình.");
        try
        {
            while (Console.ReadKey(intercept: true).Key != ConsoleKey.X) { }
        }
        catch (InvalidOperationException)
        {
            Thread.Sleep(120_000);   // chạy không có console thì giữ sống như cũ
        }
    }
#pragma warning restore CS0164
}

// ============================================================================
// Phần phức tạp — mỗi hàm là một họ ca khó, đặt SAU Main để không làm lệch
// số dòng của Main (test và checklist README đang hardcode số dòng ở trên).
//
// Dùng để thử SendKeyDemo:
//   - tìm nhãn theo prefix, nhiều nhãn cùng họ
//   - một biến rơi vào nhiều dòng trong cùng nhãn
//   - Watch đổi theo hình dạng dòng (dòng if / dòng gán / dòng thường)
//   - cắt vế phải khi trong chuỗi có  //  ;  =  if (  và nháy lồng nhau
//   - vòng lặp for/while/do/foreach: chỉ điều hướng + chụp, không bypass
//   - tên biến gần giống nhau (rc, rc2, rcTotal)
// ============================================================================
static class Complex
{
#pragma warning disable CS0164
    public static int RunAll(int rcIn)
    {
        int rc = rcIn;

    RUN_ALL:
        rc = ParsePhase(rc);
        rc = TextPhase(rc);
        rc = LoopPhase(rc);
        rc = BranchPhase(rc);
        rc = ShapePhase(rc);
        rc = NamePhase(rc);
        rc = MemberPhase(rc);
        rc = WrapPhase(rc);
        rc = SwitchPhase(rc);
        rc = RetryPhase(rc);
        rc = CodePhase(rc);
        rc = StepPhase(rc);
        rc = LongPhase(rc);
        rc = GuardPhase(rc);
        Console.WriteLine($"RUN_ALL: rc={rc}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 1. Nhãn cùng họ prefix: copy "PARSE" phải ra 5 nhãn, duyệt vòng.
    //    Cố ý KHÔNG đụng họ CHECK_INPUT / VALIDATE của Main, vì checklist
    //    README đang trông chờ số lượng nhãn cũ không đổi.
    // ------------------------------------------------------------------
    static int ParsePhase(int rcIn)
    {
        int rc = rcIn;
        int fieldCount = 0;
        string record = "";
        string header = "";
        string footer = "";

    PARSE:
        record = "id,name,amount";
        fieldCount = record.Split(',').Length;
        Console.WriteLine($"PARSE: record={record}, fieldCount={fieldCount}");
        if (fieldCount == 0) goto PARSE_FAIL;

    PARSE_HEAD:
        header = record.Split(',')[0];
        rc = header.Length > 0 ? 0 : 52;
        Console.WriteLine($"PARSE_HEAD: header={header}, rc={rc}");
        if (rc != 0) goto PARSE_FAIL;

    PARSE_BODY:
        // "fieldCount" xuất hiện 4 lần trong nhãn này -> chấm vàng, copy lại để duyệt
        fieldCount = record.Split(',').Length;
        fieldCount = fieldCount + 0;
        Console.WriteLine($"PARSE_BODY: fieldCount={fieldCount}");
        rc = fieldCount >= 3 ? 0 : 56;
        if (rc != 0) goto PARSE_FAIL;

    PARSE_BODY2:
        footer = record.Split(',')[^1];
        rc = footer.Length > 0 ? 0 : 60;
        Console.WriteLine($"PARSE_BODY2: footer={footer}, rc={rc}");

    PARSE_TAIL:
        Console.WriteLine($"PARSE_TAIL: header={header}, footer={footer}");
        goto PARSE_DONE;

    PARSE_FAIL:
        rc = rc == 0 ? 50 : rc;
        Console.WriteLine($"PARSE_FAIL: rc={rc}");

    PARSE_DONE:
        return rc;
    }

    // ------------------------------------------------------------------
    // 2. Bẫy chuỗi: cắt vế phải phải bỏ qua  //  ;  =  nằm trong nháy,
    //    giữ nguyên tiền tố @ $ @$ và cả cặp nháy.
    // ------------------------------------------------------------------
    static int TextPhase(int rcIn)
    {
        int rc = rcIn;
        string url = "";
        string msg = "";
        string list = "";
        string path = "";
        string note = "";
        string unit = "";
        string quoted = "";
        decimal amount = 12.5m;

    TEXT_URL:
        // dấu // nằm trong chuỗi — cắt comment đuôi mà không nhận biết nháy sẽ hỏng
        url = "http://example.com/orders";
        Console.WriteLine($"TEXT_URL: url={url}");

    TEXT_MSG:
        // dấu = nằm trong chuỗi — tìm dấu gán phải lấy dấu = ĐẦU TIÊN ngoài nháy
        msg = $"status=ok,rc={rc}";
        Console.WriteLine($"TEXT_MSG: msg={msg}");

    TEXT_LIST:
        // dấu ; nằm trong chuỗi — bỏ ";" kết câu phải bỏ qua cái trong nháy
        list = "a;b;c";
        Console.WriteLine($"TEXT_LIST: list={list}");

    TEXT_PATH:
        // chuỗi verbatim: giữ nguyên @ và dấu \
        path = @"C:\data\in\orders.csv";
        Console.WriteLine($"TEXT_PATH: path={path}");

    TEXT_UNIT:
        // chuỗi nội suy + verbatim gộp: Watch phải ra đúng  @$"{amount}mc/m"
        unit = @$"{amount}mc/m";
        Console.WriteLine($"TEXT_UNIT: unit={unit}");

    TEXT_QUOTED:
        // nháy lồng nhau trong chuỗi verbatim: "" là một dấu nháy, không phải kết chuỗi
        quoted = @"anh ta nói ""xong rồi"" lúc 9h";
        Console.WriteLine($"TEXT_QUOTED: quoted={quoted}");

    TEXT_FAKE_IF:
        // chuỗi chứa "if (" — KHÔNG được coi dòng này là mệnh đề if
        note = "if (rc != 0) thì bỏ qua — đây chỉ là ghi chú";
        Console.WriteLine($"TEXT_FAKE_IF: note={note}");

    TEXT_CONST:
        // vế phải là hằng số thuần — user chốt vẫn phải vào Watch
        rc = 0;
        Console.WriteLine($"TEXT_CONST: rc={rc}");

    TEXT_DONE:
        Console.WriteLine($"TEXT_DONE: rc={rc}, url={url}, unit={unit}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 3. Vòng lặp: for / while / do / foreach.
    //    App phải nhận ra là vòng lặp -> chỉ điều hướng + chụp, KHÔNG sinh
    //    lệnh SET để bypass.
    // ------------------------------------------------------------------
    static int LoopPhase(int rcIn)
    {
        int rc = rcIn;
        int sum = 0;
        int tries = 0;
        int idx = 0;
        string joined = "";

    LOOP_FOR:
        sum = 0;
        for (int i = 1; i <= 3; i++)
        {
            sum = sum + i;
            Console.WriteLine($"LOOP_FOR: i={i}, sum={sum}");
        }
        rc = sum > 0 ? 0 : 64;

    LOOP_WHILE:
        tries = 0;
        while (tries < 3)
        {
            tries = tries + 1;
            Console.WriteLine($"LOOP_WHILE: tries={tries}");
        }

    LOOP_DO:
        idx = 0;
        do
        {
            idx = idx + 1;
            Console.WriteLine($"LOOP_DO: idx={idx}");
        }
        while (idx < 2);

    LOOP_FOREACH:
        joined = "";
        foreach (var part in new[] { "x", "y", "z" })
        {
            joined = joined + part;
            Console.WriteLine($"LOOP_FOREACH: part={part}, joined={joined}");
        }

    LOOP_NESTED:
        sum = 0;
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                sum = sum + i * j;
            }
        }
        Console.WriteLine($"LOOP_NESTED: sum={sum}");

    LOOP_DONE:
        Console.WriteLine($"LOOP_DONE: rc={rc}, sum={sum}, joined={joined}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 4. Các dạng mệnh đề if — phần bypass phải xử lý được, hoặc báo rõ
    //    là không gợi ý được giá trị.
    // ------------------------------------------------------------------
    static int BranchPhase(int rcIn)
    {
        int rc = rcIn;
        int retry = 0;
        bool ready = false;
        string token = "";

    IF_SAME_LINE:
        // if và goto CÙNG dòng -> breakpoint thứ 2 phải đặt theo CỘT
        retry = 1;
        if (retry != 0) goto IF_HANDLE;

    IF_NEXT_LINE:
        // if và goto KHÁC dòng
        ready = retry > 0;
        if (!ready)
            goto IF_HANDLE;

    IF_BLOCK:
        // if có khối {} — lệnh đầu nhánh nằm trong khối
        if (retry > 99)
        {
            rc = 68;
            goto IF_HANDLE;
        }

    IF_EQUALS:
        // == chứ không phải gán: KHÔNG được coi là dòng gán
        if (retry == 1) Console.WriteLine("IF_EQUALS: retry đúng bằng 1");

    IF_NEGATED:
        // phủ định lồng ngoặc
        if (!(retry == 0)) Console.WriteLine($"IF_NEGATED: retry={retry}");

    IF_NO_SUGGEST:
        // dạng app KHÔNG gợi ý được giá trị bypass (giống if exist / if defined
        // bên batch) -> phải báo vàng và để dev tự set, không đoán bừa
        token = Environment.GetEnvironmentVariable("BIGSAMPLE_TOKEN") ?? "";
        if (string.IsNullOrEmpty(token)) Console.WriteLine("IF_NO_SUGGEST: chưa có token");

    IF_NESTED:
        if (retry > 0)
        {
            if (ready) Console.WriteLine($"IF_NESTED: retry={retry}, ready={ready}");
        }
        goto IF_DONE;

    IF_HANDLE:
        rc = rc == 0 ? 70 : rc;
        Console.WriteLine($"IF_HANDLE: rc={rc}");

    IF_DONE:
        Console.WriteLine($"IF_DONE: rc={rc}, retry={retry}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 5. Hình dạng nhãn: lệnh nằm ngay sau dấu hai chấm, nhãn có comment
    //    hoặc dòng trống chen giữa, nhãn sát nhau.
    // ------------------------------------------------------------------
    static int ShapePhase(int rcIn)
    {
        int rc = rcIn;
        int stage = 0;

    SHAPE_INLINE: stage = 1;   // lệnh NẰM CÙNG dòng nhãn -> ExecLine == LabelLine
        Console.WriteLine($"SHAPE_INLINE: stage={stage}");

    SHAPE_AFTER_COMMENT:

        // dòng trống và comment chen giữa nhãn với lệnh đầu tiên
        // -> ExecLine phải bỏ qua hết mấy dòng này
        stage = 2;
        Console.WriteLine($"SHAPE_AFTER_COMMENT: stage={stage}");

    SHAPE_BLOCK:
        {
            // dấu { ngay sau nhãn cũng phải bỏ qua khi tìm dòng thực thi
            stage = 3;
            Console.WriteLine($"SHAPE_BLOCK: stage={stage}");
        }

    SHAPE_TIGHT_A:
    SHAPE_TIGHT_B:
        // hai nhãn dính nhau, không có lệnh chen giữa
        stage = 4;
        Console.WriteLine($"SHAPE_TIGHT_B: stage={stage}");

    SHAPE_DONE:
        rc = stage > 0 ? rc : 74;
        Console.WriteLine($"SHAPE_DONE: rc={rc}, stage={stage}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 6. Tên biến gần giống nhau. Tìm biến trong thân nhãn đang so theo
    //    kiểu "có chứa", nên copy 「%RC%」 ở đây sẽ dính cả rc2 và rcTotal.
    //    Giữ ca này để thấy rõ giới hạn đó khi thử tay.
    // ------------------------------------------------------------------
    static int NamePhase(int rcIn)
    {
        int rc = rcIn;
        int rc2 = 0;
        int rcTotal = 0;

    NAME_SET:
        rc = 0;
        rc2 = 5;
        rcTotal = rc + rc2;
        Console.WriteLine($"NAME_SET: rc={rc}, rc2={rc2}, rcTotal={rcTotal}");

    NAME_CHECK:
        rc2 = rcTotal > 0 ? 0 : 78;
        Console.WriteLine($"NAME_CHECK: rc2={rc2}, rcTotal={rcTotal}");
        if (rc2 != 0) goto NAME_FAIL;

    NAME_OK:
        Console.WriteLine($"NAME_OK: rcTotal={rcTotal}");
        goto NAME_DONE;

    NAME_FAIL:
        rc = rc2;
        Console.WriteLine($"NAME_FAIL: rc={rc}");

    NAME_DONE:
        return rc;
    }

    // ------------------------------------------------------------------
    // 7. Vế trái KHÔNG phải tên biến trần: this.x, mảng, thuộc tính.
    //    Giới hạn đã biết — app chỉ Watch tên biến, không kèm vế phải.
    // ------------------------------------------------------------------
    sealed class Box
    {
        public int Rc;
        public string Name = "";
        public int[] Slots = new int[3];
        public int Doubled => Rc * 2;

        public void Fill(int seed)
        {
        BOX_FILL:
            this.Rc = seed + 1;
            this.Name = $"box-{seed}";
            this.Slots[0] = seed;
            Console.WriteLine($"BOX_FILL: Rc={Rc}, Name={Name}, Slots0={Slots[0]}");
        }
    }

    static int MemberPhase(int rcIn)
    {
        int rc = rcIn;
        var box = new Box();
        var slots = new int[3];
        var map = new Dictionary<string, int>();

    MEMBER_FILL:
        box.Fill(4);
        Console.WriteLine($"MEMBER_FILL: box.Rc={box.Rc}, doubled={box.Doubled}");

    MEMBER_ARRAY:
        slots[0] = 10;
        slots[1] = slots[0] + 5;
        Console.WriteLine($"MEMBER_ARRAY: slots0={slots[0]}, slots1={slots[1]}");

    MEMBER_DICT:
        map["rc"] = rc;
        map["total"] = slots[1];
        Console.WriteLine($"MEMBER_DICT: rc={map["rc"]}, total={map["total"]}");

    MEMBER_PROP:
        box.Rc = slots[1] > 0 ? 0 : 82;
        Console.WriteLine($"MEMBER_PROP: box.Rc={box.Rc}");

    MEMBER_DONE:
        rc = box.Rc == 0 ? rc : box.Rc;
        Console.WriteLine($"MEMBER_DONE: rc={rc}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 8. Câu lệnh trải nhiều dòng: dòng đầu KHÔNG kết thúc bằng ";" nên
    //    vế phải ở đó chỉ là mảnh cụt -> app chỉ được Watch tên biến.
    // ------------------------------------------------------------------
    static int WrapPhase(int rcIn)
    {
        int rc = rcIn;
        decimal grand = 0m;
        string report = "";
        int[] nums = { 3, 1, 2 };

    WRAP_SUM:
        grand =
            nums[0] * 1.0m +
            nums[1] * 2.0m +
            nums[2] * 3.0m;
        Console.WriteLine($"WRAP_SUM: grand={grand}");

    WRAP_TEXT:
        report = "dòng một, "
               + "dòng hai, "
               + "dòng ba";
        Console.WriteLine($"WRAP_TEXT: report={report}");

    WRAP_CALL:
        rc = CheckRange(
            (int)grand,
            0,
            100);
        Console.WriteLine($"WRAP_CALL: rc={rc}");

    WRAP_TERNARY:
        rc = rc == 0
            ? 0
            : 86;
        Console.WriteLine($"WRAP_TERNARY: rc={rc}");

    WRAP_DONE:
        return rc;
    }

    static int CheckRange(int value, int lo, int hi)
        => value >= lo && value <= hi ? 0 : 88;

    // ------------------------------------------------------------------
    // 9. switch/case bên trong thân nhãn. "case X:" trông giống nhãn nhưng
    //    KHÔNG được tính là hết phạm vi nhãn khi tìm biến.
    // ------------------------------------------------------------------
    static int SwitchPhase(int rcIn)
    {
        int rc = rcIn;
        int mode = 2;
        string modeName = "";

    SWITCH_PICK:
        mode = rc == 0 ? 2 : 9;
        switch (mode)
        {
            case 1:
                modeName = "một";
                break;
            case 2:
                modeName = "hai";
                break;
            default:
                modeName = $"khác ({mode})";
                break;
        }
        Console.WriteLine($"SWITCH_PICK: mode={mode}, modeName={modeName}");

    SWITCH_AFTER:
        // "mode" vẫn phải tìm được ở đây, không bị mấy dòng case: chặn lại
        rc = mode == 2 ? 0 : 90;
        Console.WriteLine($"SWITCH_AFTER: rc={rc}, mode={mode}");

    SWITCH_DONE:
        Console.WriteLine($"SWITCH_DONE: rc={rc}, modeName={modeName}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 10. goto nhảy LÙI — vòng retry kiểu batch. Breakpoint ở nhãn bị nhảy
    //     lại sẽ dừng nhiều lần, mỗi lần một giá trị khác nhau.
    // ------------------------------------------------------------------
    static int RetryPhase(int rcIn)
    {
        int rc = rcIn;
        int attempt = 0;
        int maxAttempt = 3;
        bool connected = false;

    RETRY_OPEN:
        attempt = attempt + 1;
        connected = attempt >= 3;
        Console.WriteLine($"RETRY_OPEN: attempt={attempt}, connected={connected}");
        if (connected) goto RETRY_OK;

    RETRY_WAIT:
        rc = attempt < maxAttempt ? 0 : 92;
        Console.WriteLine($"RETRY_WAIT: attempt={attempt}, rc={rc}");
        if (rc != 0) goto RETRY_GIVEUP;
        goto RETRY_OPEN;          // nhảy LÙI

    RETRY_OK:
        rc = 0;
        Console.WriteLine($"RETRY_OK: attempt={attempt}, rc={rc}");
        goto RETRY_DONE;

    RETRY_GIVEUP:
        Console.WriteLine($"RETRY_GIVEUP: attempt={attempt}, rc={rc}");

    RETRY_DONE:
        return rc;
    }

    // ------------------------------------------------------------------
    // 11. Đủ toán tử so sánh, ứng với NEQ / EQU / GTR / LSS / GEQ / LEQ bên
    //     batch — để thử phần gợi ý giá trị bypass cho từng dạng.
    // ------------------------------------------------------------------
    static int CodePhase(int rcIn)
    {
        int rc = rcIn;
        int code = 0;
        int limit = 5;

    CODE_NEQ:
        code = 0;
        if (code != 0) goto CODE_BAD;          // NEQ "0"  -> gợi ý 1

    CODE_EQU:
        code = 1;
        if (code == 7) goto CODE_BAD;          // EQU "7"  -> gợi ý 7

    CODE_GTR:
        code = 2;
        if (code > limit) goto CODE_BAD;       // GTR 5    -> gợi ý 6

    CODE_LSS:
        code = 9;
        if (code < limit) goto CODE_BAD;       // LSS 5    -> gợi ý 4

    CODE_GEQ:
        code = 1;
        if (code >= limit) goto CODE_BAD;      // GEQ 5    -> gợi ý 5

    CODE_LEQ:
        code = 9;
        if (code <= limit) goto CODE_BAD;      // LEQ 5    -> gợi ý 5

    CODE_OK:
        rc = 0;
        Console.WriteLine($"CODE_OK: code={code}, rc={rc}");
        goto CODE_DONE;

    CODE_BAD:
        rc = rc == 0 ? 94 : rc;
        Console.WriteLine($"CODE_BAD: code={code}, rc={rc}");

    CODE_DONE:
        return rc;
    }

    // ------------------------------------------------------------------
    // 12. Nhãn có số: copy "STEP" ra STEP1, STEP2, STEP10 — thứ tự duyệt là
    //     thứ tự DÒNG trong file, không phải thứ tự số.
    // ------------------------------------------------------------------
    static int StepPhase(int rcIn)
    {
        int rc = rcIn;
        int step = 0;

    STEP1:
        step = 1;
        Console.WriteLine($"STEP1: step={step}");

    STEP2:
        step = 2;
        Console.WriteLine($"STEP2: step={step}");

    STEP10:
        step = 10;
        Console.WriteLine($"STEP10: step={step}");

    STEP_LAST:
        rc = step == 10 ? rc : 96;
        Console.WriteLine($"STEP_LAST: rc={rc}, step={step}");
        return rc;
    }

    // ------------------------------------------------------------------
    // 13. Giá trị DÀI và dòng DÀI — để thử xem ảnh chụp có bị cắt mất giá
    //     trị trong cửa sổ Watch không (ca đợt 2 để lại, chưa làm).
    // ------------------------------------------------------------------
    static int LongPhase(int rcIn)
    {
        int rc = rcIn;
        string longText = "";
        string csvRow = "";
        decimal preciseAmount = 0m;

    LONG_TEXT:
        longText = "đây là một giá trị rất dài, cố ý dài hơn bề ngang thường thấy của cửa sổ Watch, để xem ảnh chụp có cắt mất phần đuôi hay không";
        Console.WriteLine($"LONG_TEXT: dài {longText.Length} ký tự");

    LONG_CSV:
        csvRow = "id=100001;name=Công ty TNHH Thương mại Dịch vụ Xây dựng;amount=1234567.89;date=2026-09-12;note=ghi chú dài";
        Console.WriteLine($"LONG_CSV: dài {csvRow.Length} ký tự");

    LONG_NUMBER:
        preciseAmount = 1234567.891234m + 0.000001m;
        Console.WriteLine($"LONG_NUMBER: preciseAmount={preciseAmount}");

    LONG_DONE:
        rc = longText.Length > 0 && csvRow.Length > 0 ? rc : 98;
        return rc;
    }

    // ------------------------------------------------------------------
    // 14. try / catch / finally có nhãn bên trong. goto không xuyên qua
    //     được khối try nên mỗi khối tự xử lý rồi mới ra.
    // ------------------------------------------------------------------
    static int GuardPhase(int rcIn)
    {
        int rc = rcIn;
        int parsed = 0;
        string raw = "12x";
        string state = "";

    GUARD_TRY:
        try
        {
        GUARD_PARSE:
            parsed = int.Parse(raw);
            state = "ok";
            Console.WriteLine($"GUARD_PARSE: parsed={parsed}, state={state}");
        }
        catch (FormatException ex)
        {
        GUARD_CATCH:
            parsed = -1;
            state = $"lỗi: {ex.Message}";
            Console.WriteLine($"GUARD_CATCH: parsed={parsed}, state={state}");
        }
        finally
        {
        GUARD_FINALLY:
            Console.WriteLine($"GUARD_FINALLY: parsed={parsed}");
        }

    GUARD_AFTER:
        rc = parsed >= 0 ? rc : 0;    // ca lỗi vẫn coi là chạy xong
        Console.WriteLine($"GUARD_AFTER: rc={rc}, state={state}");
        return rc;
    }
#pragma warning restore CS0164
}
