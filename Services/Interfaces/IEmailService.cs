using Services.DTOs;

namespace Services.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(EmailMessageRequest request, CancellationToken cancellationToken = default);
    Task SendVerificationEmailAsync(string toEmail, string verificationCode, CancellationToken cancellationToken = default);
    Task SendPasswordResetEmailAsync(string toEmail, string resetCode, CancellationToken cancellationToken = default);
}