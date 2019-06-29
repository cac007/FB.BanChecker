using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FB.BanChecker
{
    public class FreezeChecker
    {
        public static void Check(string apiAddress, string accessToken)
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

            //Читаем кол-во показов каждой кампании из "базы"
            var campaignImpressions = new Dictionary<string, int>();
            var ciFileName = "CampaignImpressions.txt";
            if (File.Exists(ciFileName))
            {
                campaignImpressions = File.ReadAllLines(ciFileName).ToDictionary(l => l.Split('-')[0], l => int.Parse(l.Split('-')[1]));
            }

            //Во всех работающих кампаниях получаем кол-во показов
            foreach (var c in campaignsToMonitor)
            {
                var request = new RestRequest($"{c}", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "insights{impressions,account_name,campaign_name}");

                var response = restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                var accName = json["insights"]["data"][0]["account_name"].ToString();
                var campaignName = json["insights"]["data"][0]["campaign_name"].ToString();
                var imp = int.Parse(json["insights"]["data"][0]["impressions"].ToString());
                //если уже получали кол-во показов у этой кампании
                if (campaignImpressions.ContainsKey(c))
                {
                    if (campaignImpressions[c] != imp)
                        campaignImpressions[c] = imp;
                    else
                    {
                        //ФРИЗ! Шлём уведомление об этом!
                        var msg = $"Фриз кампании {campaignName} в аккаунте {accName}!";
                        Logger.Log(msg);
                        new Mailer().SendEmailNotification(msg, "Subj!");
                    }
                }
                else
                {
                    campaignImpressions.Add(c, imp);
                }
            }

            //Записываем все полученные кол-ва показов в "базу"
            File.Delete(ciFileName);
            File.AppendAllLines(ciFileName, campaignImpressions.Select(ci => $"{ci.Key}-{ci.Value}"));
        }
    }
}