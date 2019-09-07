namespace FB.BanChecker
{
    public class AccountRecord
    {
        public AccountRecord(string record)
        {
            var s = record.Split(new[] { ',' });
            Account = s[1];
            Token = s[2];
            ProxyAddress = s[3];
            ProxyPort = s[4];
            ProxyLogin = s[5];
            ProxyPassword = s[6];
            Comment = s[7];
        }

        public string Account { get; }
        public string Token { get; }
        public string ProxyAddress { get; }
        public string ProxyPort { get; }
        public string ProxyLogin { get; }
        public string ProxyPassword { get; }
        public string Comment { get; }
    }
}
