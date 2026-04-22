using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using static Vortice.Direct2D1.D2D1;



namespace Direct2DCircleDemo;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}