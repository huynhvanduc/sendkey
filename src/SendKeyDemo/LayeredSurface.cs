using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickShot;

/// <summary>
/// Đẩy một Bitmap (Format32bppArgb) lên cửa sổ WS_EX_LAYERED qua UpdateLayeredWindow.
/// Hàm này tự lo cả vị trí + kích thước cửa sổ trong cùng 1 lệnh — rẻ hơn nhiều so với
/// việc set Form.Bounds (SetWindowPos) rồi để hệ thống tự phát WM_PAINT riêng.
/// </summary>
internal static class LayeredSurface
{
    public static void Update(IntPtr hwnd, Bitmap frame, Point screenLocation)
    {
        int w = frame.Width, h = frame.Height;
        if (w <= 0 || h <= 0) return;

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down DIB: khớp thứ tự hàng của BitmapData khi copy
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            hBitmap = Native.CreateDIBSection(screenDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return;

            var srcData = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                CopyPremultiplied(srcData, bits, w, h);
            }
            finally
            {
                frame.UnlockBits(srcData);
            }

            oldBitmap = Native.SelectObject(memDc, hBitmap);

            var ptDst = new Native.POINT(screenLocation.X, screenLocation.Y);
            var size = new Native.SIZE(w, h);
            var ptSrc = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // GDI layered window cần alpha premultiplied (RGB đã nhân sẵn với A/255),
    // trong khi Format32bppArgb của GDI+ là non-premultiplied — phải tự nhân tay.
    private static void CopyPremultiplied(BitmapData src, IntPtr destBits, int w, int h)
    {
        int srcStride = src.Stride;
        int destStride = w * 4;

        byte[] srcBuf = new byte[srcStride * h];
        Marshal.Copy(src.Scan0, srcBuf, 0, srcBuf.Length);
        byte[] destBuf = new byte[destStride * h];

        for (int y = 0; y < h; y++)
        {
            int sRow = y * srcStride;
            int dRow = y * destStride;
            for (int x = 0; x < w; x++)
            {
                int si = sRow + x * 4;
                int di = dRow + x * 4;
                byte b = srcBuf[si + 0];
                byte g = srcBuf[si + 1];
                byte r = srcBuf[si + 2];
                byte a = srcBuf[si + 3];

                destBuf[di + 0] = (byte)(b * a / 255);
                destBuf[di + 1] = (byte)(g * a / 255);
                destBuf[di + 2] = (byte)(r * a / 255);
                destBuf[di + 3] = a;
            }
        }

        Marshal.Copy(destBuf, 0, destBits, destBuf.Length);
    }
}
