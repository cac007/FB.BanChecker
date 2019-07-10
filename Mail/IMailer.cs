namespace FB.BanChecker
{
    public interface IMailer
    {
        void SendEmailNotification(string subj, string msg);
    }
}