using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FB.BanChecker
{
    public class FreezeChecker
    {
        private IMailer _mailer;

        public FreezeChecker(IMailer mailer)
        {
            _mailer=mailer;
        }
        public void Check(string apiAddress, string accessToken)
        {
            var restClient = new RestClient(apiAddress);
            //Ищем все работающие кампании
            var adAccounts = File.ReadAllLines("AdAccounts.txt");
            var campaignsToMonitor = new HashSet<string>();
            foreach (var adAccount in adAccounts)
            {
                var request = new RestRequest($"act_{adAccount}", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("fields", "account_status");
                var response = restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                if (json["account_status"].ToString()=="2") //Аккаунт забанен!
                {
                    var banMsg=$"Аккаунт {adAccount} забанен!";
                    Logger.Log(banMsg);
                    _mailer.SendEmailNotification(banMsg,"Subj!");
                    continue;
                }

                request = new RestRequest($"act_{adAccount}/campaigns", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "name");
                request.AddQueryParameter("effective_status", "['ACTIVE']");
                response = restClient.Execute(request);
                json = (JObject)JsonConvert.DeserializeObject(response.Content);
                foreach (var d in json["data"])
                {
                    Logger.Log($"В аккаунте {adAccount} найдена кампания {d["name"]} c id {d["id"]}");
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
            var msg = new StringBuilder();
            foreach (var c in campaignsToMonitor)
            {
                //Сначала чекаем, есть ли работающие адсеты в кампании!
                var request = new RestRequest($"{c}/adsets", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "ads{status},status,name");
                var response = restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                if (json["data"].Count()==0)
                {
                    Logger.Log($"В кампании {c} нет адсетов!");
                    continue;
                }
                if (json["data"].All(adset=>adset["ads"]==null))
                {
                    Logger.Log($"В кампании {c} нет объявлений!");
                    continue;
                }
                if (json["data"].All(adset=>adset["status"].ToString()=="PAUSED"))
                {
                    Logger.Log($"В кампании {c} нет работающих адсетов, все остановлены!");
                    continue;
                }

                if (json["data"].All(adset=>adset["ads"]["data"].All(ads=>ads["status"].ToString()=="PAUSED")))
                {
                    Logger.Log($"В кампании {c} нет работающих объявлений, все остановлены!");
                    continue;
                }
                
                //Нашли работающую кампанию, проверяем показы
                request = new RestRequest($"{c}/insights", Method.GET);
                request.AddQueryParameter("access_token", accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "impressions,account_name,campaign_name");

                response = restClient.Execute(request);
                json = (JObject)JsonConvert.DeserializeObject(response.Content);
                var accName = json["data"][0]["account_name"].ToString();
                var campaignName = json["data"][0]["campaign_name"].ToString();
                var imp = int.Parse(json["data"][0]["impressions"].ToString());
                //если уже получали кол-во показов у этой кампании
                if (campaignImpressions.ContainsKey(c))
                {
                    if (campaignImpressions[c] != imp)
                    {
                        campaignImpressions[c] = imp;
                        Logger.Log($"Кампания {campaignName} крутит, всё с ней хорошо!");
                    }
                    else
                    {
                        //ФРИЗ! Шлём уведомление об этом!
                        var freezeMsg = $"Фриз кампании {campaignName} в аккаунте {accName}!";
                        msg.AppendLine(freezeMsg);
                        Logger.Log(freezeMsg);
                    }
                }
                else
                {
                    campaignImpressions.Add(c, imp);
                }
            }

            //Шлём одно письмо по всем фризам
            if (msg.Length > 0)
                _mailer.SendEmailNotification("Обнаружен ФРИЗ кампаний!", msg.ToString());

            //Записываем все полученные кол-ва показов в "базу"
            File.WriteAllLines(ciFileName, campaignImpressions.Select(ci => $"{ci.Key}-{ci.Value}"));
        }
    }
}