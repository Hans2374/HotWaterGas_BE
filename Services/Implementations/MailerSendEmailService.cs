using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class MailerSendEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MailerSendEmailService> _logger;

    public MailerSendEmailService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<MailerSendEmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendVerificationEmailAsync(string toEmail, string verificationCode, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessageRequest
        {
            ToEmail = toEmail,
            Subject = "Verify your HotWaterGas account",
            HtmlBody = BuildVerificationHtml(verificationCode),
            TextBody = BuildVerificationText(verificationCode)
        };

        return SendEmailAsync(message, cancellationToken);
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetCode, CancellationToken cancellationToken = default)
    {
        var message = new EmailMessageRequest
        {
            ToEmail = toEmail,
            Subject = "Reset your HotWaterGas password",
            HtmlBody = BuildPasswordResetHtml(resetCode),
            TextBody = BuildPasswordResetText(resetCode)
        };

        return SendEmailAsync(message, cancellationToken);
    }

    public async Task SendEmailAsync(EmailMessageRequest request, CancellationToken cancellationToken = default)
    {
        var apiToken = _configuration["MailerSend:ApiToken"];
        var fromEmail = _configuration["MailerSend:FromEmail"];
        var fromName = _configuration["MailerSend:FromName"];

        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(fromName))
        {
          var missing = new List<string>();
          if (string.IsNullOrWhiteSpace(apiToken)) missing.Add("ApiToken");
          if (string.IsNullOrWhiteSpace(fromEmail)) missing.Add("FromEmail");
          if (string.IsNullOrWhiteSpace(fromName)) missing.Add("FromName");

          _logger.LogError("[MailerSend.Config] Missing configuration values: {Missing}", string.Join(", ", missing));
          throw new InvalidOperationException($"MailerSend configuration missing: {string.Join(", ", missing)}");
        }

        var recipientName = string.IsNullOrWhiteSpace(request.ToName) ? request.ToEmail : request.ToName;

        _logger.LogInformation("[MailerSend] Sending email to {Email} Subject={Subject}", request.ToEmail, request.Subject);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "email");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        httpRequest.Content = JsonContent.Create(new
        {
            from = new { email = fromEmail, name = fromName },
            to = new[] { new { email = request.ToEmail, name = recipientName } },
            subject = request.Subject,
            html = request.HtmlBody,
            text = request.TextBody
        }, options: JsonOptions);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation(
          "[MailerSend] HTTP Status={StatusCode} To={Email} Subject={Subject}",
          (int)response.StatusCode,
          request.ToEmail,
          request.Subject);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "[MailerSend] Email failed to {Email} StatusCode={StatusCode} Response={Response}",
                request.ToEmail,
                (int)response.StatusCode,
                responseBody);

            throw new InvalidOperationException($"MailerSend failed with status code {(int)response.StatusCode}.");
        }

        _logger.LogInformation("[MailerSend] Email success to {Email} Subject={Subject}", request.ToEmail, request.Subject);
    }

    private static string BuildVerificationHtml(string verificationCode)
    {
        return $$"""
<!doctype html>
<html lang="en">
  <body style="margin:0;padding:0;background:#f6f7fb;font-family:Arial,Helvetica,sans-serif;color:#1f2937;">
    <div style="max-width:640px;margin:0 auto;padding:32px 16px;">
      <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;padding:32px;">
        <h1 style="margin:0 0 16px;font-size:24px;line-height:1.2;">Verify your HotWaterGas account</h1>
        <p style="margin:0 0 24px;font-size:16px;line-height:1.6;">Use the verification code below to confirm your email address. The code expires in 15 minutes.</p>
        <div style="display:inline-block;padding:16px 24px;border-radius:12px;background:#111827;color:#ffffff;font-size:28px;letter-spacing:6px;font-weight:700;">{{verificationCode}}</div>
        <p style="margin:24px 0 0;font-size:14px;line-height:1.6;color:#6b7280;">If you did not create a HotWaterGas account, you can ignore this email.</p>
        <p style="margin:12px 0 0;font-size:14px;line-height:1.6;color:#6b7280;">Need help? Reply to this email and our support team will assist you.</p>
      </div>
    </div>
  </body>
</html>
""";
    }

    private static string BuildVerificationText(string verificationCode)
    {
        return $"Verify your HotWaterGas account\n\nUse this verification code: {verificationCode}\nThis code expires in 15 minutes.\n\nIf you did not create a HotWaterGas account, you can ignore this email.";
    }

    private static string BuildPasswordResetHtml(string resetCode)
    {
        return $$"""
<!doctype html>
<html lang="en">
  <body style="margin:0;padding:0;background:#f6f7fb;font-family:Arial,Helvetica,sans-serif;color:#1f2937;">
    <div style="max-width:640px;margin:0 auto;padding:32px 16px;">
      <div style="background:#ffffff;border:1px solid #e5e7eb;border-radius:16px;padding:32px;">
        <h1 style="margin:0 0 16px;font-size:24px;line-height:1.2;">Reset your HotWaterGas password</h1>
        <p style="margin:0 0 24px;font-size:16px;line-height:1.6;">Use the password reset code below to continue. The code expires in 15 minutes.</p>
        <div style="display:inline-block;padding:16px 24px;border-radius:12px;background:#111827;color:#ffffff;font-size:28px;letter-spacing:6px;font-weight:700;">{{resetCode}}</div>
        <p style="margin:24px 0 0;font-size:14px;line-height:1.6;color:#6b7280;">If you did not request this reset, you can ignore this message and your password will remain unchanged.</p>
        <p style="margin:12px 0 0;font-size:14px;line-height:1.6;color:#6b7280;">For your security, never share this code with anyone.</p>
      </div>
    </div>
  </body>
</html>
""";
    }

    private static string BuildPasswordResetText(string resetCode)
    {
        return $"Reset your HotWaterGas password\n\nUse this password reset code: {resetCode}\nThis code expires in 15 minutes.\n\nIf you did not request this reset, ignore this email.";
    }
}