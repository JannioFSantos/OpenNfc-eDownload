using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
[assembly: AssemblyTitle("openNfc-eDownload")]
[assembly: AssemblyDescription("Download de NFC-e com A1 para SP e RS")]
[assembly: AssemblyVersion("1.0.0.0")]
namespace OpenNfceDownload
{
    internal static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--self-test") >= 0) return SelfTests.Run(args);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (var mutex = new Mutex(true, "Local\\openNfceDownload-Desktop", out created))
            {
                if (!created) { MessageBox.Show("O openNfc-eDownload já está aberto."); return 1; }
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "openNfc-eDownload", MessageBoxButtons.OK, MessageBoxIcon.Error); };
                Application.Run(new MainForm(null));
            }
            return 0;
        }
    }
}
