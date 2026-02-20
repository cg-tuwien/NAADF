
using NAADF;
using System;

namespace NAADF
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var app = new App())
                app.Run();
        }
    }
}
