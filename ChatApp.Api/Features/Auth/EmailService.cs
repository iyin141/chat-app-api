using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace ChatApp.Api.Features.Auth;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetLink);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
    {
        try
        {
            var email = new MimeMessage();
            email.From.Add(MailboxAddress.Parse(_config["Email:From"] ?? "onboarding@f1controlroomdocs.com"));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = "Password Reset Request";
            email.Body = new TextPart(TextFormat.Html) 
            { 
                Text = $"<p>You requested a password reset. Click the link below to reset your password:</p><p><a href='{resetLink}'>Reset Password</a></p><p>This link will expire in 15 minutes.</p>"
            };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_config["Email:SmtpHost"] ?? "smtp.gmail.com", int.Parse(_config["Email:SmtpPort"] ?? "587"), SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_config["Email:SmtpUser"], _config["Email:SmtpPass"]);
            
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
            
            _logger.LogInformation("Password reset email sent to {Email}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
        }
    }
}
