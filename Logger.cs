using System;
using System.IO;

namespace FB.BanChecker
{
    public static class Logger
    {
        public static void Log(string msg)
        {
            var msgWithDate=$"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToLongTimeString()}{msg}\n";
            Console.Write(msgWithDate);
            File.AppendAllText("Log.txt", msgWithDate);
        }
    }
}
