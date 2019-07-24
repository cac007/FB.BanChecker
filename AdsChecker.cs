using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FB.BanChecker
{
    public class AdsChecker
    {
        private readonly string _apiAddress;
        private readonly string _accessToken;
        private readonly IMailer _mailer;
        private readonly Navigator _nav;
        private RestClient _restClient;
        private const string _ciFileName = "CampaignImpressions.txt";

        public AdsChecker(string apiAddress, string accessToken, IMailer mailer, Navigator nav)
        {
            _apiAddress = apiAddress;
            _accessToken = accessToken;
            _mailer = mailer;
            _nav = nav;
            _restClient = new RestClient(_apiAddress);
        }

        private Dictionary<string, string> GetRunningCampaigns()
        {
            //Ищем все работающие кампании
            var adAccounts = File.ReadAllLines("AdAccounts.txt");
            var campaignsToMonitor = new Dictionary<string, string>();
            foreach (var adAccount in adAccounts)
            {
                var aac = adAccount;

                if (!int.TryParse(aac, out _)) //у нас не ID аккаунта а имя
                {
                    aac = _nav.GetAdAccountByName(aac);
                }

                var request = new RestRequest($"act_{aac}", Method.GET);
                request.AddQueryParameter("access_token", _accessToken);
                request.AddQueryParameter("fields", "account_status");
                var response = _restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                ErrorChecker.HasErrorsInResponse(json, true);
                if (json["account_status"].ToString() == "2") //Аккаунт забанен!
                {
                    var banMsg = $"Аккаунт {adAccount} забанен!";
                    Logger.Log(banMsg);
                    _mailer.SendEmailNotification(banMsg, "Subj!");
                    continue;
                }

                request = new RestRequest($"act_{aac}/campaigns", Method.GET);
                request.AddQueryParameter("access_token", _accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "name");
                request.AddQueryParameter("effective_status", "['ACTIVE']");
                response = _restClient.Execute(request);
                json = (JObject)JsonConvert.DeserializeObject(response.Content);
                ErrorChecker.HasErrorsInResponse(json, true);
                foreach (var d in json["data"])
                {
                    Logger.Log($"В аккаунте {adAccount} найдена кампания {d["name"]} c id {d["id"]}");
                    campaignsToMonitor.Add(d["id"].ToString(), adAccount);
                }
            }
            return campaignsToMonitor;
        }

        public void CheckAds()
        {
            var campaignsToMonitor = GetRunningCampaigns();

            //Читаем кол-во показов каждой кампании из "базы"
            var campaignImpressions = new Dictionary<string, int>();
            if (File.Exists(_ciFileName))
            {
                campaignImpressions = File.ReadAllLines(_ciFileName).ToDictionary(l => l.Split('-')[0], l => int.Parse(l.Split('-')[1]));
            }

            var freezeMsgs = new StringBuilder();
            var adsMsgs = new StringBuilder();
            foreach (var kvp in campaignsToMonitor)
            {
                var c = kvp.Key;
                //Сначала получаем имя кампании
                string cname = GetCampaignName(c);
                Logger.Log($"Начинаем проверку кампании {cname} в аккаунте {kvp.Value}...");

                //Сначала чекаем, есть ли работающие адсеты в кампании!
                if (!CheckIfThereAreWorkingAdsets(c, cname)) continue;

                //Нашли работающую кампанию, проверяем показы
                var request = new RestRequest($"{c}/insights", Method.GET);
                request.AddQueryParameter("access_token", _accessToken);
                request.AddQueryParameter("date_preset", "today");
                request.AddQueryParameter("fields", "impressions,account_name,campaign_name");

                var response = _restClient.Execute(request);
                var json = (JObject)JsonConvert.DeserializeObject(response.Content);
                ErrorChecker.HasErrorsInResponse(json, true);
                if (json["data"] == null || !json["data"].Any())
                {
                    Logger.Log($"!!!Похоже, что в кампании {cname} аккаунта {kvp.Value} ещё не начался открут, хотя кампания активна!");
                }
                else
                {
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
                            freezeMsgs.AppendLine(freezeMsg);
                            Logger.Log(freezeMsg);
                        }
                    }
                    else
                    {
                        campaignImpressions.Add(c, imp);
                    }
                }

                //Проверяем все объявления кампании
                request = new RestRequest($"{c}/ads", Method.GET);
                request.AddQueryParameter("access_token", _accessToken);
                request.AddQueryParameter("fields", "account_id,creative{effective_object_story_id},campaign{name},effective_status,issues_info,status");
                response = _restClient.Execute(request);
                json = (JObject)JsonConvert.DeserializeObject(response.Content);
                ErrorChecker.HasErrorsInResponse(json, true);

                var adCreatives = new HashSet<string>();
                var ads = json["data"];
                foreach (var ad in ads)
                {
                    if (!adCreatives.Contains(ad["creative"]["id"].ToString()))
                        adCreatives.Add(ad["creative"]["id"].ToString());
                    //Получили ID поста, теперь сохраним все данные по негативу
                    var storyId = ad["creative"]["effective_story_id"].ToString();
                    SavePostFeedback(storyId);

                    var status = ad["effective_status"].ToString();
                    if (status == "DISAPPROVED")
                    {
                        adsMsgs.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус DISAPPROVED");
                        //TODO:сделать перезалив и уведомление по мылу
                    }
                    else if (status == "WITH_ISSUES")
                    {
                        adsMsgs.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус WITH_ISSUES: {ad["issues_info"]}");

                    }
                }
                Logger.Log($"Проверили все статусы объяв в кампании {cname}.");

                var accId = campaignsToMonitor[c];
                if (!int.TryParse(accId, out _))
                {
                    accId = _nav.GetAdAccountByName(accId);
                }
                //Проверяем все креативы кампании, вычленяем из них ссылки и страницы
                request = new RestRequest($"act_{accId}/adcreatives", Method.GET);
                request.AddQueryParameter("access_token", _accessToken);
                request.AddQueryParameter("fields", "object_story_spec,object_story_id");
                response = _restClient.Execute(request);
                json = (JObject)JsonConvert.DeserializeObject(response.Content);
                ErrorChecker.HasErrorsInResponse(json, true);

                var activeCreatives = json["data"].Where(j => adCreatives.Contains(j["id"].ToString())).ToList();
                var pages = new HashSet<string>();
                var links = new HashSet<string>();
                foreach (var adCr in activeCreatives)
                {
                    var osp = adCr["object_story_spec"];
                    if (osp != null)
                    {
                        pages.Add(osp["page_id"].ToString());
                        if (osp["video_data"] != null) //видеокрео
                            links.Add(osp["video_data"]["call_to_action"]["value"]["link"].ToString());
                        else if (osp["link_data"] != null) //картинка
                            links.Add(osp["link_data"]["link"].ToString());
                    }
                    else //креатив на PostID
                    {
                        request = new RestRequest($"{adCr["object_story_id"]}", Method.GET);
                        request.AddQueryParameter("access_token", _accessToken);
                        request.AddQueryParameter("fields", "call_to_action");
                        response = _restClient.Execute(request);
                        json = (JObject)JsonConvert.DeserializeObject(response.Content);
                        if (ErrorChecker.HasErrorsInResponse(json))
                        {
                            //крео отлетело к праотцам!
                            if (json["error"]["message"].ToString().Contains("does not exist"))
                            {
                                var postIdMsg = $"Объявление на PostID {adCr["object_story_id"]} из кампании {cname} аккаунта {kvp.Value} отлетело к праотцам! Мир его праху...";
                                Logger.Log(postIdMsg);
                                adsMsgs.AppendLine(postIdMsg);
                                continue;
                            }
                        }
                        links.Add(json["call_to_action"]["value"]["link"].ToString());
                        pages.Add(adCr["object_story_id"].ToString().Split('_')[0]);
                    }
                }

                foreach (var p in pages)
                {
                    CheckIfPageIsBanned(p);
                }

                foreach (var l in links)
                {
                    CheckIfLinkIsBanned(l);
                }

                Logger.Log($"Закончили проверку кампании {cname} в аккаунте {kvp.Value}.");
            }

            if (adsMsgs.Length > 0)
            {
                Logger.Log(adsMsgs.ToString());
                _mailer.SendEmailNotification("Некорректный статус у объявлений!", adsMsgs.ToString());
            }

            //Шлём одно письмо по всем фризам
            if (freezeMsgs.Length > 0)
                _mailer.SendEmailNotification("Обнаружен ФРИЗ кампаний!", freezeMsgs.ToString());

            //Записываем все полученные кол-ва показов в "базу"
            File.WriteAllLines(_ciFileName, campaignImpressions.Select(ci => $"{ci.Key}-{ci.Value}"));
        }

        private void SavePostFeedback(string storyId)
        {
            var request = new RestRequest($"{storyId}/insights", Method.GET);
            request.AddQueryParameter("access_token", _accessToken);
            request.AddQueryParameter("metric", "post_negative_feedback_by_type,post_reactions_by_type_total");
            var response = _restClient.Execute(request);
            var json = (JObject)JsonConvert.DeserializeObject(response.Content);
            ErrorChecker.HasErrorsInResponse(json, true);
            File.WriteAllText($"{storyId}.json",json.ToString());
        }

        private bool CheckIfThereAreWorkingAdsets(string c, string cname)
        {
            var request = new RestRequest($"{c}/adsets", Method.GET);
            request.AddQueryParameter("access_token", _accessToken);
            request.AddQueryParameter("date_preset", "today");
            request.AddQueryParameter("fields", "ads{status},status,name");
            var response = _restClient.Execute(request);
            var json = (JObject)JsonConvert.DeserializeObject(response.Content);
            ErrorChecker.HasErrorsInResponse(json, true);
            if (json["data"].Count() == 0)
            {
                Logger.Log($"В кампании {cname} нет адсетов!");
                return false;
            }
            if (json["data"].All(adset => adset["ads"] == null))
            {
                Logger.Log($"В кампании {cname} нет объявлений!");
                return false;
            }
            if (json["data"].All(adset => adset["status"].ToString() == "PAUSED"))
            {
                Logger.Log($"В кампании {cname} нет работающих адсетов, все остановлены!");
                return false;
            }
            if (json["data"].All(adset => adset["ads"]["data"].All(ads => ads["status"].ToString() == "PAUSED")))
            {
                Logger.Log($"В кампании {cname} нет работающих объявлений, все остановлены!");
                return false;
            }
            return true;
        }

        private string GetCampaignName(string c)
        {
            var request = new RestRequest(c, Method.GET);
            request.AddQueryParameter("access_token", _accessToken);
            request.AddQueryParameter("fields", "name");
            var response = _restClient.Execute(request);
            var json = (JObject)JsonConvert.DeserializeObject(response.Content);
            ErrorChecker.HasErrorsInResponse(json, true);
            return json["name"].ToString();
        }

        public bool CheckIfLinkIsBanned(string link)
        {
            var request = new RestRequest("", Method.POST);
            request.AddParameter("access_token", _accessToken);
            request.AddParameter("scrape", "true");
            request.AddParameter("id", link);
            var response = _restClient.Execute(request);
            if (response.Content.Contains("disallowed")) //сайт забанен!
            {
                var msg = $"Domain {link} was banned on Facebook!";
                Logger.Log(msg);
                _mailer.SendEmailNotification(msg, "Subj!");
                return true;
            }
            else
            {
                Logger.Log($"Domain {link} is not banned.");
                return false;
            }
        }

        public bool CheckIfPageIsBanned(string pageName)
        {
            var request = new RestRequest($"me/accounts", Method.GET);
            request.AddQueryParameter("access_token", _accessToken);
            request.AddQueryParameter("type", "page");
            request.AddQueryParameter("fields", "name,is_published,access_token");
            var response = _restClient.Execute(request);
            var json = (JObject)JsonConvert.DeserializeObject(response.Content);
            ErrorChecker.HasErrorsInResponse(json, true);
            var msg = new StringBuilder();
            bool result = false;
            foreach (var p in json["data"])
            {
                //Если мониторим, то проверяем, опубликована или нет
                if (pageName != p["id"].ToString() && pageName != p["name"].ToString())
                    continue;
                if (bool.Parse(p["is_published"].ToString()))
                {
                    Logger.Log($"Страница {p["name"]} не снята с публикации, ол гут!");
                    continue;
                }

                //Страница не опубликована! Пытаемся опубликовать
                request = new RestRequest(p["id"].ToString(), Method.POST);
                request.AddParameter("access_token", p["access_token"].ToString());
                request.AddParameter("is_published", "true");
                response = _restClient.Execute(request);
                var publishJson = (JObject)JsonConvert.DeserializeObject(response.Content);
                if (ErrorChecker.HasErrorsInResponse(publishJson))
                {
                    //невозможно опубликовать страницу, вероятно, она забанена!
                    var pageMsg = $"Страница {p["name"]} не опубликована и, вероятно, забанена!";
                    msg.AppendLine(pageMsg);
                    Logger.Log(pageMsg);
                    result = true;
                }
                else
                {
                    //уведомим пользователя, что мы опубликовали страницу после снятия с публикации
                    var pageMsg = $"Страница {p["name"]} была заново опубликована после снятия с публикации!";
                    msg.AppendLine(pageMsg);
                    Logger.Log(pageMsg);
                }
            }

            //Шлём одно письмо по всем страницам
            if (msg.Length > 0)
                _mailer.SendEmailNotification("Некоторые страницы были сняты с публикации!", msg.ToString());
            return result;
        }
    }
}