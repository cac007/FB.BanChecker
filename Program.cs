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
            IMailer mailer;
#if DEBUG
            mailer=new FakeMailer();
#else
            mailer=new Mailer();
#endif

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Не указан access_token!");
                return;
            }
            if (config.GetValue<bool>("check_domains"))
                new DomainsChecker(mailer).Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_freeze"))
                new FreezeChecker(mailer).Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_ads"))
                new AdsChecker(mailer).Check(apiAddress, accessToken);
            if (config.GetValue<bool>("check_pages"))
                new PagesChecker(mailer).Check(apiAddress, accessToken);
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            var msg="Произошла непредвиденная ошибка:"+e.ExceptionObject;
            Console.WriteLine(msg);
            Logger.Log(msg);
        }
    }
}