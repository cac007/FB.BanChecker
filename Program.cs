using Microsoft.Extensions.Configuration;
using System;
using System.Text;

namespace FB.BanChecker
{
    class Program
    {
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Console.OutputEncoding = Encoding.UTF8;
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
            var accessToken = config.GetValue<string>("access_token");
            var apiAddress = config.GetValue<string>("fbapi_address");
            IMailer mailer;
#if DEBUG
            mailer = new FakeMailer();
#else
            mailer=new Mailer();
#endif

            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Не указан access_token!");
                return;
            }

            var nav = new Navigator(accessToken, apiAddress);
            new AdsChecker(apiAddress, accessToken, mailer, nav).CheckAds();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            var msg = "Произошла непредвиденная ошибка:" + e.ExceptionObject;
            Console.WriteLine(msg);
            Logger.Log(msg);
        }
    }
}