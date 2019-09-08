using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FB.BanChecker
{
    public class AdsChecker
    {
        private readonly string _apiAddress;
        private readonly IMailer _mailer;
        private const string _ciFileName = "CampaignImpressions.txt";

        public AdsChecker(string apiAddress, IMailer mailer)
        {
            _apiAddress = apiAddress;
            _mailer = mailer;
        }

        public async Task CheckAdsAsync()
        {
            var campaignImpressions = GetCampaignsImpressions();
            var accountRecords = await File.ReadAllLinesAsync(Path.GetFullPath(@"../accounts.txt"));
            foreach (var record in accountRecords)
            {
                var ar = new AccountRecord(record);

                if (!ar.Comment.ToLowerInvariant().StartsWith("ywb")) continue;

                var re = new RequestExecutor(
                    _apiAddress, ar.Token, ar.ProxyAddress, ar.ProxyPort, ar.ProxyLogin, ar.ProxyPassword);
                var ff = new FacebookFacade(re);
                Logger.Log($"Начинаем проверку записи {ar.Comment}.");

                var banned = await ff.CheckIfAccountIsBanned(ar.Account);
                if (banned)
                {
                    var banMsg = $"{ar.Comment}: Аккаунт {ar.Account} забанен!";
                    Logger.Log(banMsg);
                    await _mailer.SendEmailNotificationAsync(banMsg, "Subj!");
                    Logger.Log($"Проверка записи {ar.Comment} закончена.");
                    continue;
                }
                var campaignsToMonitor = await ff.GetRunningCampaignsAsync(ar.Account);
                if (campaignsToMonitor.Count==0)
                    Logger.Log("Не найдено работающих кампаний!");
                var mailMessage = new StringBuilder();
                foreach (var c in campaignsToMonitor)
                {
                    //Сначала получаем имя кампании
                    string cname = await ff.GetCampaignNameAsync(c);
                    Logger.Log($"Начинаем проверку кампании {cname} в аккаунте {ar.Account}...");

                    //Сначала чекаем, есть ли работающие адсеты в кампании!
                    if (!await ff.CheckIfThereAreWorkingAdsetsAsync(c, cname))
                    {
                        Logger.Log($"В кампании {cname} в аккаунте {ar.Account} нет работающих адсетов, пропускаем.");
                        continue;
                    }

                    var json = await ff.GetCampaignInsights(c);
                    //Нашли работающую кампанию, проверяем показы
                    if (json["data"] == null || !json["data"].Any())
                    {
                        Logger.Log($"!!!Похоже, что в кампании {cname} аккаунта {ar.Account} ещё не начался открут, хотя кампания активна!");
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
                                mailMessage.AppendLine(freezeMsg);
                                Logger.Log(freezeMsg);
                            }
                        }
                        else
                        {
                            campaignImpressions.Add(c, imp);
                        }
                    }

                    var ads = await ff.GetAllCampaignAdsAsync(c);

                    var adCreatives = new HashSet<string>();
                    foreach (var ad in ads)
                    {
                        var creoId = ad["creative"]["id"].ToString();
                        if (!adCreatives.Contains(creoId))
                            adCreatives.Add(creoId);
                        //Получили ID поста, теперь сохраним все данные по негативу
                        var storyId = ad["creative"]["effective_object_story_id"].ToString();
                        var postFeedback=await ff.GetPostFeedbackAsync(storyId);
                        if (postFeedback.ToString().Contains("does not exist"))
                        {
                            var msg=$"Крео {storyId} отлетело к праотцам! Перезаливай!";
                            Logger.Log(msg);
                            mailMessage.AppendLine(msg);
                        }
                        else
                        {
                            ErrorChecker.HasErrorsInResponse(json, true);
                            await File.WriteAllTextAsync($"{storyId}.json", json.ToString());
                        }

                        var status = ad["effective_status"].ToString();
                        if (status == "DISAPPROVED")
                        {
                            mailMessage.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус DISAPPROVED");
                            //TODO:сделать перезалив и уведомление по мылу
                        }
                        else if (status == "WITH_ISSUES")
                        {
                            mailMessage.AppendLine($"Объявление из кампании {ad["campaign"]["name"]} аккаунта {ad["account_id"]} перешло в статус WITH_ISSUES: {ad["issues_info"]}");

                        }
                    }
                    Logger.Log($"Проверили все статусы объяв в кампании {cname}.");

                    //Проверяем все креативы кампании, вычленяем из них ссылки и страницы
                    json = await ff.GetAccountCreativesAsync(ar.Account);

                    var activeCreatives = json["data"].Where(j => adCreatives.Contains(j["id"].ToString())).ToList();
                    var pages = new HashSet<string>();
                    var links = new HashSet<string>();
                    foreach (var adCr in activeCreatives)
                    {
                        var t = GetPageAndCreoLiks(adCr);
                        if (!string.IsNullOrEmpty(t.pageId))
                            pages.Add(t.pageId);
                        if (!string.IsNullOrEmpty(t.link))
                            links.Add(t.link);
                    }

                    foreach (var p in pages)
                    {
                        var pageIsBanned = await ff.CheckIfPageIsBannedAsync(p);
                        if (pageIsBanned)
                            mailMessage.AppendLine($"Страница {p} в аккаунте {ar.Account} не опубликована и, вероятно, забанена!");
                    }

                    foreach (var l in links)
                    {
                        var linkIdBanned = await ff.CheckIfLinkIsBannedAsync(l);
                        if (linkIdBanned)
                            mailMessage.AppendLine($"Ссылка {l} в аккаунте {ar.Account} забанена на FB!");
                    }

                    Logger.Log($"Закончили проверку кампании {cname} в аккаунте {ar.Account}.");
                }

                //Шлём одно письмо по всем фризам
                if (mailMessage.Length > 0)
                {
                    mailMessage.Insert(0, $"При проверке записи {ar.Comment} возникли ошибки:");
                    await _mailer.SendEmailNotificationAsync(
                        $"Обнаружены ошибки в акке {ar.Account}", mailMessage.ToString());
                }

                Logger.Log($"Проверка записи {ar.Comment} закончена.");
                //Записываем все полученные кол-ва показов в "базу"
                await File.WriteAllLinesAsync(_ciFileName, campaignImpressions.Select(ci => $"{ci.Key}-{ci.Value}"));
            }
        }

        private static (string pageId, string link) GetPageAndCreoLiks(JToken adCr)
        {
            var osp = adCr["object_story_spec"];
            string pageId, link = string.Empty;
            pageId = osp["page_id"].ToString();
            if (osp["video_data"] != null) //видеокрео
                link = osp["video_data"]["call_to_action"]["value"]["link"].ToString();
            else if (osp["link_data"] != null) //картинка
                link = osp["link_data"]["link"].ToString();
            return (pageId, link);
        }


        private static Dictionary<string, int> GetCampaignsImpressions()
        {
            //Читаем кол-во показов каждой кампании из "базы"
            var campaignImpressions = new Dictionary<string, int>();
            if (File.Exists(_ciFileName))
            {
                campaignImpressions = File.ReadAllLines(_ciFileName).ToDictionary(l => l.Split('-')[0], l => int.Parse(l.Split('-')[1]));
            }

            return campaignImpressions;
        }
    }
}