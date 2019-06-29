using System;
using System.IO;

namespace FB.BanChecker
{
    public static class Logger
    {
        public static void Log(string msg)
        {
            File.AppendAllText("Log.txt", $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}{msg}\n");
        }
    }
}
