using Microsoft.Extensions.Configuration;
using System;

namespace FB.BanChecker
{
    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
            var accessToken = config.GetValue<string>("access_token");
            var apiAddress = config.GetValue<string>("fbapi_address");

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Не указан access_token!");
                return;
            }
            if (config.GetValue<bool>("check_domains"))
                DomainsChecker.Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_freeze"))
                FreezeChecker.Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_ads"))
                AdsChecker.Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_pages"))
                PagesChecker.Check(apiAddress, accessToken);
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Console.WriteLine("Произошла непредвиденная ошибка:"+e.ExceptionObject);
        }
    }
}