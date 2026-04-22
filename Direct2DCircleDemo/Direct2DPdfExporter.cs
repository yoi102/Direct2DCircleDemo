using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using static Vortice.Direct2D1.D2D1;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectWrite.DWrite;

namespace Direct2DCircleDemo;

internal static class Direct2DPdfExporter
{
    // A4: 210 x 297 mm -> 595 x 842 DIP @ 96 DPI
    private const float A4WidthDip = 595.0f;
    private const float A4HeightDip = 842.0f;

    private const uint STGM_WRITE = 0x00000001;
    private const uint STGM_SHARE_EXCLUSIVE = 0x00000010;
    private const uint STGM_CREATE = 0x00001000;
    private const int STREAM_SEEK_SET = 0;

    // CLSID: PrintDocumentPackageTargetFactory
    private static readonly Guid CLSID_PrintDocumentPackageTargetFactory =
        new("348EF17D-6C81-4982-92B4-EE188A43867A");

    private static readonly Vortice.Direct3D.FeatureLevel[] s_featureLevels =
    {
        Vortice.Direct3D.FeatureLevel.Level_11_1,
        Vortice.Direct3D.FeatureLevel.Level_11_0,
        Vortice.Direct3D.FeatureLevel.Level_10_1,
        Vortice.Direct3D.FeatureLevel.Level_10_0
    };

    public static void ExportSimplePdf(string pdfPath, string printerName = "Microsoft Print to PDF")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);

        using ID3D11Device d3dDevice = CreateD3D11Device();
        using IDXGIDevice dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        using ID2D1Factory1 d2dFactory = D2D1CreateFactory<ID2D1Factory1>();
        using ID2D1Device d2dDevice = d2dFactory.CreateDevice(dxgiDevice);
        using ID2D1DeviceContext d2dContext = d2dDevice.CreateDeviceContext();
        using IWICImagingFactory wicFactory = new IWICImagingFactory();

        DWriteCreateFactory(out IDWriteFactory dwFactory).CheckError();
        using (dwFactory)
        {
            using var titleFormat = dwFactory.CreateTextFormat(
                "Segoe UI",
                Vortice.DirectWrite.FontWeight.SemiBold,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                24.0f);

            using var bodyFormat = dwFactory.CreateTextFormat(
                "Segoe UI",
                Vortice.DirectWrite.FontWeight.Normal,
                Vortice.DirectWrite.FontStyle.Normal,
                Vortice.DirectWrite.FontStretch.Normal,
                12.0f);

            SHCreateStreamOnFileEx(
                pdfPath,
                STGM_CREATE | STGM_WRITE | STGM_SHARE_EXCLUSIVE,
                0,
                true,
                null,
                out IStream outputStream);

            try
            {
                outputStream.Seek(0, STREAM_SEEK_SET, IntPtr.Zero);

                var factoryType = Type.GetTypeFromCLSID(CLSID_PrintDocumentPackageTargetFactory, throwOnError: true)!;
                var targetFactory = (IPrintDocumentPackageTargetFactory)Activator.CreateInstance(factoryType)!;

                IntPtr packageTargetPtr = IntPtr.Zero;
                try
                {
                    targetFactory.CreateDocumentPackageTargetForPrintJob(
                        printerName,
                        Path.GetFileNameWithoutExtension(pdfPath),
                        outputStream,
                        null,
                        out packageTargetPtr);

                    if (packageTargetPtr == IntPtr.Zero)
                        throw new InvalidOperationException("CreateDocumentPackageTargetForPrintJob returned null.");

                    using var packageTarget = new ComObject(packageTargetPtr);

                    var printProps = new PrintControlProperties
                    {
                        FontSubset = PrintFontSubsetMode.Default,
                        RasterDPI = 300.0f,
                        ColorSpace = ColorSpace.Srgb
                    };

                    using var printControl = d2dDevice.CreatePrintControl(
                        wicFactory,
                        packageTarget,
                        printProps);

                    using var commandList = d2dContext.CreateCommandList();

                    d2dContext.Target = commandList;
                    d2dContext.BeginDraw();
                    d2dContext.Clear(Colors.White);

                    using var blackBrush = d2dContext.CreateSolidColorBrush(Colors.Black);
                    using var blueBrush = d2dContext.CreateSolidColorBrush(Colors.DarkBlue);

                    using var titleLayout = dwFactory.CreateTextLayout(
                        "Direct2D -> PDF",
                        titleFormat,
                        451.0f,
                        40.0f);

                    using var bodyLayout = dwFactory.CreateTextLayout(
                        "This page is generated with Direct2D PrintControl.\r\n中文测试：这是一段 DirectWrite 输出到 PDF 的文本。",
                        bodyFormat,
                        451.0f,
                        100.0f);

                    d2dContext.DrawTextLayout(new Vector2(72, 72), titleLayout, blackBrush);
                    d2dContext.DrawTextLayout(new Vector2(72, 120), bodyLayout, blackBrush);

                    d2dContext.DrawEllipse(
                        new Ellipse(new Vector2(150, 260), 60, 60),
                        blueBrush,
                        2.5f);

                    d2dContext.FillEllipse(
                        new Ellipse(new Vector2(300, 260), 40, 40),
                        blueBrush);

                    d2dContext.EndDraw().CheckError();
                    commandList.Close().CheckError();

                    ulong tag1;
                    ulong tag2;
                    printControl.AddPage(
                        commandList,
                        new Vortice.Mathematics.Size(A4WidthDip, A4HeightDip),
                        null,
                        out tag1,
                        out tag2);

                    printControl.Close();

                    outputStream.Commit(0);
                }
                finally
                {
                    if (packageTargetPtr != IntPtr.Zero)
                    {
                        // ComObject.Dispose() 会 Release 一次；这里不重复 Release
                    }

                    if (targetFactory != null)
                    {
                        Marshal.FinalReleaseComObject(targetFactory);
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(outputStream);
            }
        }
    }

    private static ID3D11Device CreateD3D11Device()
    {
        DeviceCreationFlags creationFlags = DeviceCreationFlags.BgraSupport;

        Result hr = D3D11CreateDevice(
            IntPtr.Zero,
            DriverType.Hardware,
            creationFlags,
            s_featureLevels,
            out ID3D11Device tempDevice,
            out _,
            out ID3D11DeviceContext tempContext);

        if (hr.Failure)
        {
            D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Warp,
                creationFlags,
                s_featureLevels,
                out tempDevice,
                out _,
                out tempContext).CheckError();
        }

        tempContext.Dispose();
        return tempDevice;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateStreamOnFileEx(
        string pszFile,
        uint grfMode,
        uint dwAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool fCreate,
        IStream? pstmTemplate,
        out IStream ppstm);

    [ComImport]
    [Guid("D2959BF7-B31B-4A3D-9600-712EB1335BA4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPrintDocumentPackageTargetFactory
    {
        void CreateDocumentPackageTargetForPrintJob(
            [MarshalAs(UnmanagedType.LPWStr)] string printerName,
            [MarshalAs(UnmanagedType.LPWStr)] string jobName,
            [MarshalAs(UnmanagedType.Interface)] IStream jobOutputStream,
            [MarshalAs(UnmanagedType.Interface)] IStream? jobPrintTicketStream,
            out IntPtr docPackageTarget);
    }
}