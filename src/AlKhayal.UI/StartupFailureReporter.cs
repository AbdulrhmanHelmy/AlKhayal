using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AlKhayal.UI
{
    internal static class StartupFailureReporter
    {
        public static bool TryLog(Exception exception)
        {
            var details = DateTime.UtcNow.ToString("o") + " Startup failure" + Environment.NewLine
                + exception + Environment.NewLine + Environment.NewLine;
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AlKhayal", "Logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "startup.log"), details, Encoding.UTF8);
                return true;
            }
            catch (Exception loggingError)
            {
                Trace.TraceError(details + "Could not write startup log: " + loggingError);
                return false;
            }
        }
    }
}
