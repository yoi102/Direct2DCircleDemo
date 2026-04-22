using System.ComponentModel;
using System.Drawing.Printing;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;
using static Vortice.DirectWrite.DWrite;

namespace Direct2DCircleDemo;

public partial class Form1 : Form
{
    private ID2D1Factory? _factory;
    private ID2D1HwndRenderTarget? _renderTarget;
    private ID2D1SolidColorBrush? _brush;
    private IDWriteFactory? _dwFactory;

    private readonly Button _btnExportPdf;

    public Form1()
    {
        InitializeComponent();

        _btnExportPdf = new Button
        {
            Text = "导出 PDF (Direct2D)",
            Dock = DockStyle.Bottom,
            Height = 40
        };
        _btnExportPdf.Click += btnExportPdf_Click;
        Controls.Add(_btnExportPdf);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        _factory = D2D1CreateFactory<ID2D1Factory>();
        DWriteCreateFactory(out _dwFactory);

        var hwndProps = new HwndRenderTargetProperties
        {
            Hwnd = Handle,
            PixelSize = new SizeI(
                Math.Max(ClientSize.Width, 1),
                Math.Max(ClientSize.Height, 1)),
            PresentOptions = PresentOptions.None
        };

        _renderTarget = _factory.CreateHwndRenderTarget(
            new RenderTargetProperties(),
            hwndProps);

        _brush = _renderTarget.CreateSolidColorBrush(Colors.Red);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_renderTarget != null)
        {
            _renderTarget.Resize(new SizeI(
                Math.Max(ClientSize.Width, 1),
                Math.Max(ClientSize.Height, 1)));

            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_renderTarget == null || _brush == null)
            return;

        _renderTarget.BeginDraw();

        _renderTarget.Clear(Colors.White);

        _renderTarget.DrawEllipse(
            new Ellipse(new Vector2(180, 140), 80f, 80f),
            _brush,
            3.0f);

        _renderTarget.FillEllipse(
            new Ellipse(new Vector2(340, 140), 45f, 45f),
            _brush);

        _renderTarget.EndDraw();
    }

    private void btnExportPdf_Click(object sender, EventArgs e)
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = "direct2d-output.pdf",
            Title = "保存 PDF"
        };

        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        Direct2DPdfExporter.ExportSimplePdf(sfd.FileName);
        MessageBox.Show(this, "导出完成");
    }
    private void ExportPdfWithDirect2D(string pdfPath)
    {
        if (_factory == null || _dwFactory == null)
            throw new InvalidOperationException("Direct2D / DirectWrite 尚未初始化。");

        string printerName = FindPdfPrinterName();

        IntPtr hdc = CreateDC("WINSPOOL", printerName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDC failed.");

        try
        {
            var docInfo = new DOCINFOW
            {
                cbSize = Marshal.SizeOf<DOCINFOW>(),
                lpszDocName = "Direct2D PDF Demo",
                lpszOutput = pdfPath
            };

            if (StartDoc(hdc, ref docInfo) <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "StartDoc failed.");

            bool pageStarted = false;
            try
            {
                if (StartPage(hdc) <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "StartPage failed.");

                pageStarted = true;

                int pageWidthPx = GetDeviceCaps(hdc, HORZRES);
                int pageHeightPx = GetDeviceCaps(hdc, VERTRES);
                int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                int dpiY = GetDeviceCaps(hdc, LOGPIXELSY);

                float pageWidthDip = pageWidthPx * 96.0f / Math.Max(dpiX, 1);
                float pageHeightDip = pageHeightPx * 96.0f / Math.Max(dpiY, 1);

                var rtProps = new RenderTargetProperties
                {
                    PixelFormat = new Vortice.DCommon.PixelFormat(
                        Format.B8G8R8A8_UNorm,
                        Vortice.DCommon.AlphaMode.Ignore),
                    DpiX = dpiX,
                    DpiY = dpiY
                };

                using var dcRenderTarget = _factory.CreateDCRenderTarget(rtProps);
                dcRenderTarget.BindDC(hdc, new RawRect(0, 0, pageWidthPx, pageHeightPx));

                using var blackBrush = dcRenderTarget.CreateSolidColorBrush(Colors.Black);
                using var blueBrush = dcRenderTarget.CreateSolidColorBrush(Colors.DarkBlue);

                using var titleFormat = _dwFactory.CreateTextFormat("Segoe UI", 24.0f);
                using var bodyFormat = _dwFactory.CreateTextFormat("Segoe UI", 14.0f);

                dcRenderTarget.BeginDraw();
                dcRenderTarget.Clear(Colors.White);

                dcRenderTarget.DrawText(
                    "Direct2D -> PDF",
                    titleFormat,
                    new Rect(72, 72, pageWidthDip - 144, 40),
                    blackBrush);

                dcRenderTarget.DrawText(
                    "This PDF is generated by drawing Direct2D content onto a printer DC.\r\n" +
                    "下面的圆和文字都不是 GDI 直接画的，而是 Direct2D / DirectWrite 画出来的。",
                    bodyFormat,
                    new Rect(72, 130, pageWidthDip - 144, 80),
                    blackBrush);

                dcRenderTarget.DrawEllipse(
                    new Ellipse(new Vector2(160, 280), 60, 60),
                    blueBrush,
                    2.5f);

                dcRenderTarget.FillEllipse(
                    new Ellipse(new Vector2(320, 280), 40, 40),
                    blueBrush);

                dcRenderTarget.DrawText(
                    $"Page Size: {pageWidthPx} x {pageHeightPx} px\r\n" +
                    $"Printer DPI: {dpiX} x {dpiY}",
                    bodyFormat,
                    new Rect(72, 380, pageWidthDip - 144, 60),
                    blackBrush);

                dcRenderTarget.EndDraw();

                if (EndPage(hdc) <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "EndPage failed.");

                pageStarted = false;

                if (EndDoc(hdc) <= 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "EndDoc failed.");
            }
            catch(Exception ex)
            {
                if (pageStarted)
                {
                    EndPage(hdc);
                }
                AbortDoc(hdc);
                throw;
            }
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    private static string FindPdfPrinterName()
    {
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (string.Equals(name, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
                return name;
        }

        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (name.Contains("Print to PDF", StringComparison.OrdinalIgnoreCase))
                return name;
        }

        throw new InvalidOperationException("未找到“Microsoft Print to PDF”打印机。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _brush?.Dispose();
            _renderTarget?.Dispose();
            _dwFactory?.Dispose();
            _factory?.Dispose();
        }

        base.Dispose(disposing);
    }

    // ===== Win32 printing interop =====

    private const int HORZRES = 8;
    private const int VERTRES = 10;
    private const int LOGPIXELSX = 88;
    private const int LOGPIXELSY = 90;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOCINFOW
    {
        public int cbSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszDocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszOutput;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszDatatype;
        public int fwType;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDC(
        string pwszDriver,
        string pwszDevice,
        string? pszPort,
        IntPtr pdm);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int StartDoc(IntPtr hdc, [In] ref DOCINFOW lpdi);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int EndDoc(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int AbortDoc(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int StartPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int EndPage(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);
}