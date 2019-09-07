using System;
using System.Threading.Tasks;

namespace FB.BanChecker
{
    public class FakeMailer : IMailer
    {
        public Task SendEmailNotificationAsync(string subj, string msg)
        {
            Console.WriteLine($"{DateTime.Now.ToShortDateString()} Шлём \"письмо\":{subj} {msg}");
            return Task.CompletedTask;
        }
    }
}