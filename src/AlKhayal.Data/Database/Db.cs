using System;
using System.IO;

namespace AlKhayal.Data.Database
{
    public static class Db
    {
        public static string ConnectionStringFor(string filePath)
        {
            return "Data Source=" + filePath + ";Version=3;Foreign Keys=True;";
        }

        public static string DefaultDatabasePath()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlKhayal");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "alkhayal.db");
        }
    }
}
