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
            if(config.GetValue<bool>("check_domains"))
                DomainsChecker.Check(apiAddress, accessToken);
            if(config.GetValue<bool>("check_freeze"))
                FreezeChecker.Check(apiAddress, accessToken);
            if(config.GetValue<bool>("check_ads"))
                AdsChecker.Check(apiAddress,accessToken);
        }
    }
}