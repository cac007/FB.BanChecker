using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FB.BanChecker
{
    public class FacebookFacade
    {
        private readonly RequestExecutor _re;

        public FacebookFacade(RequestExecutor re)
        {
            _re = re;
        }



        public async Task<bool> CheckIfAccountIsBanned(string adAccount)
        {
            var request = new RestRequest($"act_{adAccount}", Method.GET);
            request.AddQueryParameter("fields", "account_status");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            if (json["account_status"].ToString() == "2") //Аккаунт забанен!
            {
                return true;
            }
            return false;
        }

        public async Task<HashSet<string>> GetRunningCampaignsAsync(string adAccount)
        {
            //Ищем все работающие кампании
            var campaignsToMonitor = new HashSet<string>();

            var request = new RestRequest($"act_{adAccount}/campaigns", Method.GET);
            request.AddQueryParameter("date_preset", "today");
            request.AddQueryParameter("fields", "name");
            request.AddQueryParameter("effective_status", "['ACTIVE']");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            foreach (var d in json["data"])
            {
                Logger.Log($"В аккаунте {adAccount} найдена работающая кампания {d["name"]} c id {d["id"]}");
                campaignsToMonitor.Add(d["id"].ToString());
            }
            return campaignsToMonitor;
        }

        public async Task<JObject> GetCampaignInsights(string c)
        {
            var request = new RestRequest($"{c}/insights", Method.GET);
            request.AddQueryParameter("date_preset", "today");
            request.AddQueryParameter("fields", "impressions,account_name,campaign_name");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            return json;
        }

        public async Task<bool> CheckIfThereAreWorkingAdsetsAsync(string c, string cname)
        {
            var request = new RestRequest($"{c}/adsets", Method.GET);
            request.AddQueryParameter("date_preset", "today");
            request.AddQueryParameter("fields", "ads{status},status,name");
            var json = await _re.ExecuteRequestAsync(request);
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

        public async Task<List<JToken>> GetAllCampaignAdsAsync(string c)
        {
            //Проверяем все объявления кампании
            var request = new RestRequest($"{c}/ads", Method.GET);
            request.AddQueryParameter("fields", "account_id,creative{effective_object_story_id},campaign{name},effective_status,issues_info,status");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            return json["data"].ToList();
        }

        public async Task<JObject> GetAccountCreativesAsync(string accId)
        {
            var request = new RestRequest($"act_{accId}/adcreatives", Method.GET);
            request.AddQueryParameter("fields", "object_story_spec,object_story_id");
            var json=await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            return json;
        }

        public async Task<JObject> GetObjectStoryAsync(string objectStoryId)
        {
            var request = new RestRequest($"{objectStoryId}", Method.GET);
            request.AddQueryParameter("fields", "call_to_action");
            return await _re.ExecuteRequestAsync(request);
        }

        public async Task SavePostFeedbackAsync(string storyId)
        {
            var request = new RestRequest($"{storyId}/insights", Method.GET);
            request.AddQueryParameter("metric", "post_negative_feedback_by_type,post_reactions_by_type_total");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            await File.WriteAllTextAsync($"{storyId}.json", json.ToString());
        }

        public async Task<bool> CheckIfPageIsBannedAsync(string pageName)
        {
            var request = new RestRequest($"me/accounts", Method.GET);
            request.AddQueryParameter("type", "page");
            request.AddQueryParameter("fields", "name,is_published,access_token");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);

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
                var publishJson = await _re.ExecuteRequestAsync(request,false);
                if (ErrorChecker.HasErrorsInResponse(publishJson))
                {
                    //невозможно опубликовать страницу, вероятно, она забанена!
                    var pageMsg = $"Страница {p["name"]} не опубликована и, вероятно, забанена!";
                    Logger.Log(pageMsg);
                    result = true;
                }
                else
                {
                    //уведомим пользователя, что мы опубликовали страницу после снятия с публикации
                    var pageMsg = $"Страница {p["name"]} была заново опубликована после снятия с публикации!";
                    Logger.Log(pageMsg);
                }
            }

            return result;
        }
        public async Task<bool> CheckIfLinkIsBannedAsync(string link)
        {
            var request = new RestRequest("", Method.POST);
            request.AddParameter("scrape", "true");
            request.AddParameter("id", link);
            var json=await _re.ExecuteRequestAsync(request);
            if (json.ToString().Contains("disallowed")) //сайт забанен!
            {
                var msg = $"Domain {link} was banned on Facebook!";
                Logger.Log(msg);
                return true;
            }
            else
            {
                Logger.Log($"Domain {link} is not banned.");
                return false;
            }
        }
        public async Task<string> GetCampaignNameAsync(string c)
        {
            var request = new RestRequest(c, Method.GET);
            request.AddQueryParameter("fields", "name");
            var json = await _re.ExecuteRequestAsync(request);
            ErrorChecker.HasErrorsInResponse(json, true);
            return json["name"].ToString();
        }
    }
}
