using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FB.BanChecker
{
    public class PagesChecker
    {
        public static void Check(string apiAddress, string accessToken)
        {
            //Берём все страницы для мониторинга
            var pages = File.ReadAllLines("Pages.txt").ToHashSet();

            var restClient = new RestClient(apiAddress);
            var request = new RestRequest($"me/accounts", Method.GET);
            request.AddQueryParameter("access_token", accessToken);
            request.AddQueryParameter("type", "page");
            request.AddQueryParameter("fields", "name,is_published,access_token");
            var response = restClient.Execute(request);
            var json = (JObject)JsonConvert.DeserializeObject(response.Content);
            var msg = new StringBuilder();
            foreach (var p in json["data"])
            {
                //Если мониторим, то проверяем, опубликована или нет
                if (!pages.Contains(p["id"].ToString()) && !pages.Contains(p["name"].ToString()))
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
                response = restClient.Execute(request);
                var publishJson = (JObject)JsonConvert.DeserializeObject(response.Content);
                if (publishJson["error"] != null)
                {
                    //невозможно опубликовать страницу, вероятно, она забанена!
                    var pageMsg = $"Страница {p["name"]} не опубликована и, вероятно, забанена!";
                    msg.AppendLine(pageMsg);
                    Logger.Log(pageMsg);
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
                new Mailer().SendEmailNotification("Некоторые страницы были сняты с публикации!", msg.ToString());
        }
    }
}