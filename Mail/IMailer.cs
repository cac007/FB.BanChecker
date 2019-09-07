using System.Threading.Tasks;

namespace FB.BanChecker
{
    public interface IMailer
    {
        Task SendEmailNotificationAsync(string subj, string msg);
    }
}