namespace FB.BanChecker
{
    public class FakeMailer : IMailer
    {
        public void SendEmailNotification(string subj, string msg) { }
    }
}