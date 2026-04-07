using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace LPM.Services;

public class EmailService
{
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUser;
    private readonly string _smtpPassword;
    private readonly string _fromName;
    private readonly string _baseUrl;

    public EmailService(IConfiguration config)
    {
        _smtpHost     = config["Email:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort     = int.TryParse(config["Email:SmtpPort"], out var p) ? p : 587;
        _smtpUser     = config["Email:SmtpUser"] ?? "";
        _smtpPassword = config["Email:SmtpPassword"] ?? "";
        _fromName     = config["Email:FromName"] ?? "LPM System";
        _baseUrl      = (config["Email:BaseUrl"] ?? "").TrimEnd('/');
    }

    public async Task<bool> SendVerificationCodeAsync(string toEmail, string code, string userName)
    {
        if (string.IsNullOrWhiteSpace(_smtpUser) || string.IsNullOrWhiteSpace(_smtpPassword))
        {
            Console.WriteLine("[EMAIL] SMTP not configured — skipping verification email");
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_fromName, _smtpUser));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = $"LPM — Your verification code: {code}";

            var builder = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family:system-ui,sans-serif;max-width:480px;margin:0 auto;padding:32px;'>
                        <div style='background:linear-gradient(135deg,#4f46e5,#7c3aed);border-radius:16px;padding:32px;color:#fff;text-align:center;'>
                            <h2 style='margin:0 0 8px;font-size:1.4rem;'>Login Verification</h2>
                            <p style='margin:0;opacity:.8;font-size:.9rem;'>LPM System</p>
                        </div>
                        <div style='padding:24px 0;text-align:center;'>
                            <p>Hi <strong>{userName}</strong>,</p>
                            <p>Enter this code on the login page to verify your device:</p>
                            <div style='margin:24px 0;'>
                                <div style='display:inline-block;padding:16px 40px;background:#f8fafc;border:2px solid #e2e8f0;border-radius:12px;letter-spacing:8px;font-size:2rem;font-weight:800;color:#1e293b;font-family:monospace;'>
                                    {code}
                                </div>
                            </div>
                            <p style='color:#64748b;font-size:.85rem;'>This code expires in 10 minutes.</p>
                            <p style='color:#94a3b8;font-size:.75rem;'>If you didn't request this, you can ignore this email.</p>
                        </div>
                    </div>"
            };

            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpUser, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine($"[EMAIL] Magic link sent to {toEmail} for {userName}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL] Failed to send magic link to {toEmail}: {ex.Message}");
            return false;
        }
    }
}
