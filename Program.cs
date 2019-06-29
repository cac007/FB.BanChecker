using Microsoft.Extensions.Configuration;
using RestSharp;
using System;

namespace FB.BanChecker
{
    class Program
    {
        static void Main(string[] args)
        {
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

            DomainsChecker.Check(apiAddress, accessToken);
            FreezeChecker.Check(apiAddress, accessToken);
            AdsChecker.Check(apiAddress,accessToken);
        }
    }
}