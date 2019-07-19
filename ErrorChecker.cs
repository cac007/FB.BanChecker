using Newtonsoft.Json.Linq;
using System;

namespace FB.BanChecker
{
    public static class ErrorChecker
    {
        public static bool HasErrorsInResponse(JObject json,bool throwException=false)
        {
            var error = json["error"]?["message"].ToString();
            if (!string.IsNullOrEmpty(error))
            {
                var msg=$"Ошибка при попытке выполнить запрос:{json["error"]}!";
                Logger.Log(msg);
                if (throwException)
                    throw new Exception(msg);
                return true;
            }
            return false;
        }

        public static bool VideoIsNotReadyResponse(JObject json)
        {
            var error = json["error"]?["message"].ToString();
            var eut=json["error"]?["error_user_title"].ToString();
            var eum=json["error"]?["error_user_msg"].ToString();
            if (eut== "Video not ready for use in an ad")
            {
                return true;
            }
            return false;
        }

    }
}