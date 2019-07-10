using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FB.BanChecker
{
    public class AdsChecker
    {
        private readonly IMailer _mailer;

        public AdsChecker(IMailer mailer)
        {
            _mailer = mailer;
        }

        public void Check(string apiAddress, string accessToken)
        {
            var restClient = new RestClient(apiAddress);
            //Ищем все работающие кампании
            var adAccounts = File.ReadAllLines("AdAccounts.txt");
            var campaignsToMonitor = new HashSet<string>();
            foreach (var adAccount in adAccounts)
            {
                var request = new RestRequest($"act_{adAccount}/campaigns", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("effective_status", "['ACTIVE']");
                var response = restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                foreach (var d in json["data"])
                {
                    campaignsToMonitor.Add(d["id"].ToString());
                }
            }

            //Во всех работающих кампаниях получаем инфу по всем объявам
            var msg = new StringBuilder();
            foreach (var c in campaignsToMonitor)
            {
                var request = new RestRequest($"{c}/ads", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("fields", "account_id,campaign{name},effective_status,issues_info");

                var response = restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                foreach (var ad in json["data"])
                {
                    var status = ad["effective_status"].ToString();
                    if (status == "DISAPPROVED")
                    {
                        msg.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус DISAPPROVED");
                    }
                    else if (status == "WITH_ISSUES")
                    {
                        msg.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус WITH_ISSUES: {ad["issues_info"]}");

                    }
                }
                Logger.Log($"Проверили все объявы в кампании {c}.");
            }
            if (msg.Length > 0)
            {
                Logger.Log(msg.ToString());
                _mailer.SendEmailNotification("Некорректный статус у объявлений!", msg.ToString());
            }
        }

    }
}