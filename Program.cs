using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PhotoDuplicateFinder
{
    internal static class Program
    {
        internal static readonly Icon AppIcon = new Icon(typeof(Program), "PhotoDuplicateFinder.ico");

        [DllImport("kernel32.dll")]
        private static extern ushort SetThreadUILanguage(ushort languageId);

        [STAThread]
        private static void Main()
        {
            var english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            CultureInfo.CurrentUICulture = english;
            SetThreadUILanguage((ushort)english.LCID);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
