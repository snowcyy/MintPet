using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MintPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "MintPet.SingleInstance.7C1F", out createdNew))
            {
                if (!createdNew) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    LogCrash(e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    LogCrash(e.ExceptionObject as Exception);
                };

                try
                {
                    AutoStart.Migrate();
                    Application.Run(new PetWindow());
                }
                catch (Exception ex)
                {
                    LogCrash(ex);
                }
            }
        }

        private static void LogCrash(Exception exception)
        {
            try
            {
                string folder = PetSettings.Folder;
                Directory.CreateDirectory(folder);

                StringBuilder text = new StringBuilder();
                text.AppendLine("MintPet crash log");
                text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                text.AppendLine(Redact.Text(exception == null ? "(no exception object)" : exception.ToString(), null));
                text.AppendLine();

                File.AppendAllText(Path.Combine(folder, "error.log"), text.ToString(), new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
