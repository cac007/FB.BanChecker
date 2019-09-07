using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FB.BanChecker
{
    public class Navigator
    {
        private readonly Dictionary<string, string> _accByNameDict = new Dictionary<string, string>();
        private readonly RequestExecutor _re;
        private const string _adAccountsFileName = "accountnames.txt";
        public Navigator(RequestExecutor re)
        {
            _re = re;
        }

        public async Task<string> GetFanPageNameAsync(string pageId)
        {
            var request = new RestRequest(pageId, Method.GET);
            request.AddQueryParameter("fields", "name,is_published,access_token");
            var json = await _re.ExecuteRequestAsync(request);
            return json["name"].ToString();
        }

        public async Task<string> GetFanPageBackedInsagramAccountAsync(string pageId)
        {
            var request = new RestRequest(pageId, Method.GET);
            request.AddQueryParameter("fields", "access_token");
            var json = await _re.ExecuteRequestAsync(request);
            var token = json["access_token"].ToString();
            request = new RestRequest($"{pageId}/page_backed_instagram_accounts", Method.GET);
            request.AddQueryParameter("access_token", token);
            json = await _re.ExecuteRequestAsync(request);
            if (!json["data"].Any())
            {
                request = new RestRequest($"{pageId}/page_backed_instagram_accounts", Method.POST);
                request.AddParameter("access_token", token);
                json = await _re.ExecuteRequestAsync(request);
                return json["id"].ToString();
            }
            return json["data"][0]["id"].ToString();
        }

        public async Task<string> SelectFanPageAsync()
        {
            var request = new RestRequest($"me/accounts", Method.GET);
            request.AddQueryParameter("fields", "name,is_published,access_token");
            request.AddQueryParameter("type", "page");
            var json = await _re.ExecuteRequestAsync(request);

            var pages = json["data"].OrderBy(p => p["name"].ToString()).ToList();
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                Console.WriteLine($"{i + 1}. {p["name"]} - {p["is_published"]}");
            }

        PageStart:
            int index;
            bool goodRes;
            do
            {
                Console.Write("Выберите страницу, введя её номер, и нажмите Enter:");
                var readIndex = Console.ReadLine();
                goodRes = int.TryParse(readIndex, out index);
                index--;
                if (index < 0 || index > pages.Count - 1) goodRes = false;
            }
            while (!goodRes);

            var selectedPage = pages[index];

            if (!bool.Parse(selectedPage["is_published"].ToString()))
            {
                Console.Write($"Страница {selectedPage["name"]} не опубликована! Опубликовать?");
                if (YesNoSelector.ReadAnswerEqualsYes())
                {
                    //Страница не опубликована! Пытаемся опубликовать
                    request = new RestRequest(selectedPage["id"].ToString(), Method.POST);
                    request.AddParameter("access_token", selectedPage["access_token"].ToString());
                    request.AddParameter("is_published", "true");
                    var publishJson = await _re.ExecuteRequestAsync(request, false);
                    if (publishJson["error"] != null)
                    {
                        //невозможно опубликовать страницу, вероятно, она забанена!
                        Console.WriteLine($"Страница {selectedPage["name"]} не опубликована и, вероятно, забанена!");
                        goto PageStart;
                    }
                    else
                    {
                        //уведомим пользователя, что мы опубликовали страницу после снятия с публикации
                        Console.WriteLine($"Страница {selectedPage["name"]} была заново опубликована после снятия с публикации!");
                        return selectedPage["id"].ToString();
                    }
                }
                else
                    goto PageStart;
            }
            return selectedPage["id"].ToString();
        }

        public async Task<string> SelectBusinessManagerAsync()
        {
            var bms = await GetAllBmsAsync();

            for (int i = 0; i < bms.Count; i++)
            {
                var bm = bms[i];
                Console.WriteLine($"{i + 1}. {bm["name"]}");
            }

            bool goodRes;
            int index;
            do
            {
                Console.Write("Выберите БМ, введя его номер, и нажмите Enter:");
                var readIndex = Console.ReadLine();
                goodRes = int.TryParse(readIndex, out index);
                if (index > bms.Count) goodRes = false;
            }
            while (!goodRes);
            return bms[index - 1]["id"].ToString();
        }


        public async Task<string> GetAdAccountsBusinessManagerAsync(string acc)
        {
            var req = new RestRequest($"act_{acc}", Method.GET);
            req.AddQueryParameter("fields", "business");
            var respJson = await _re.ExecuteRequestAsync(req);
            ErrorChecker.HasErrorsInResponse(respJson, true);
            return respJson["business"]["id"].ToString();
        }

        public async Task<string> SelectAdAccountAsync(string bmid, bool includeBanned = false)
        {
            var accounts = await GetBmsAdAccountsAsync(bmid, includeBanned);

            for (int i = 0; i < accounts.Count; i++)
            {
                var acc = accounts[i];
                Console.WriteLine($"{i + 1}. {acc["name"]}");
            }

            int index;
            bool goodRes;
            do
            {
                Console.Write("Выберите РК, введя его номер, и нажмите Enter:");
                var readIndex = Console.ReadLine();
                goodRes = int.TryParse(readIndex, out index);
                if (index > accounts.Count - 1) goodRes = false;
            }
            while (!goodRes);
            return accounts[index]["id"].ToString();
        }

        public async Task<string> GetAdAccountByNameAsync(string name)
        {
            if (_accByNameDict.Count == 0 || !_accByNameDict.ContainsKey(name))
            {
                _accByNameDict.Clear();
                var bms = await GetAllBmsAsync();
                foreach (var bm in bms)
                {
                    var adAccounts = await GetBmsAdAccountsAsync(bm["id"].ToString(), true);
                    adAccounts.ForEach(acc => _accByNameDict.Add(acc["name"].ToString(), acc["id"].ToString()));
                }
                await File.WriteAllLinesAsync(
                    _adAccountsFileName, _accByNameDict.Select(kvp => $"{kvp.Key}-{kvp.Value}"));
            }
            if (_accByNameDict.ContainsKey(name))
                return _accByNameDict[name];
            return string.Empty;
        }

        private async Task<List<JToken>> GetAllBmsAsync()
        {
            var request = new RestRequest($"me/businesses", Method.GET);
            var json = await _re.ExecuteRequestAsync(request);
            var bms = json["data"].ToList();
            return bms;
        }

        public async Task<List<JToken>> GetBmsAdAccountsAsync(string bmid, bool includeBanned = false)
        {
            var request = new RestRequest($"{bmid}/client_ad_accounts", Method.GET);
            request.AddQueryParameter("fields", "name,account_status");
            var json = await _re.ExecuteRequestAsync(request);
            var accounts = json["data"].ToList();
            request = new RestRequest($"{bmid}/owned_ad_accounts", Method.GET);
            request.AddQueryParameter("fields", "name,account_status");
            json = await _re.ExecuteRequestAsync(request);
            accounts.AddRange(json["data"].ToList());
            //Исключаем забаненные
            if (!includeBanned)
                accounts = accounts.Where(acc => acc["account_status"].ToString() != "2").ToList();
            return accounts;
        }
    }
}