using Microsoft.Extensions.Configuration;
using System;
using System.Text;
using System.Threading.Tasks;

namespace FB.BanChecker
{
    class Program
    {
        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            Console.OutputEncoding = Encoding.UTF8;
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();
            var apiAddress = config.GetValue<string>("fbapi_address");
            IMailer mailer;
#if DEBUG
            mailer = new FakeMailer();
#else
            mailer=new Mailer();
#endif

            await new AdsChecker(apiAddress, mailer).CheckAdsAsync();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            var msg = "Произошла непредвиденная ошибка:" + e.ExceptionObject;
            Console.WriteLine(msg);
            Logger.Log(msg);
        }
    }
}