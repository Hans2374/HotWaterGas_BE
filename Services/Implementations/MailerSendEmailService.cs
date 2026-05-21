using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Services.DTOs;
using Services.Interfaces;

namespace Services.Implementations;

public class MailerSendEmailService : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string DefaultProductImageUrl =
        "https://res.cloudinary.com/do4qn1p2e/image/upload/v1234567890/products/placeholder.png";

    private static readonly Dictionary<string, string> PaymentStatusVietnamese = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAID"]       = "Đã thanh toán",
        ["PENDING"]    = "Đang chờ",
        ["CANCELLED"]  = "Đã hủy",
        ["FAILED"]     = "Thất bại",
        ["PROCESSING"] = "Đang xử lý"
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly FrontendOptions _frontendOptions;
    private readonly ILogger<MailerSendEmailService> _logger;

    public MailerSendEmailService(
        HttpClient httpClient,
        IConfiguration configuration,
        IOptions<FrontendOptions> frontendOptions,
        ILogger<MailerSendEmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _frontendOptions = frontendOptions.Value;
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

    public Task SendFulfillmentEmailAsync(FulfillmentEmailRequest request, CancellationToken cancellationToken = default)
    {
        var subject = $"Hoàn tất đơn hàng #{request.OrderCode} — HotWaterGas";

        var resolvedItems = request.Items.Select(item =>
        {
            var resolvedUrl = BuildAbsoluteImageUrl(item.ProductImageUrl);
            _logger.LogDebug(
                "[MailerSend.Fulfillment] ProductImageResolved. ProductId={ProductId} ProductName={ProductName} "
                + "OriginalImageUrl={OriginalImageUrl} ResolvedAbsoluteImageUrl={ResolvedAbsoluteImageUrl}",
                item.ProductId,
                item.ProductName,
                item.ProductImageUrl ?? "(null)",
                resolvedUrl);
            return item.WithResolvedImageUrl(resolvedUrl);
        }).ToList();

        var resolvedRequest = request.WithResolvedItems(resolvedItems);

        var message = new EmailMessageRequest
        {
            ToEmail = request.ToEmail,
            ToName = request.ToName,
            Subject = subject,
            HtmlBody = BuildFulfillmentHtml(resolvedRequest),
            TextBody = BuildFulfillmentText(resolvedRequest)
        };

        _logger.LogInformation(
            "[MailerSend.Fulfillment] Queuing fulfillment email. To={Email} OrderCode={OrderCode} ItemCount={ItemCount}",
            request.ToEmail,
            request.OrderCode,
            request.Items.Count);

        var htmlPreview = message.HtmlBody.Length > 200 ? message.HtmlBody[..200] + "..." : message.HtmlBody;
        _logger.LogDebug("[MailerSend.Fulfillment] HTML body preview (first 200 chars): {HtmlPreview}", htmlPreview);
        _logger.LogDebug("[MailerSend.Fulfillment] Subject: {Subject}", message.Subject);

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

    private string BuildLogoUrl()
    {
        var baseUrl = _frontendOptions.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return string.Empty;
        }
        return $"{baseUrl.TrimEnd('/')}/icon.png";
    }

    private static string LocalizePaymentStatus(string status)
    {
        return PaymentStatusVietnamese.TryGetValue(status, out var localized)
            ? localized
            : status;
    }

    private string BuildAbsoluteImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return DefaultProductImageUrl;
        }

        imageUrl = imageUrl.Trim();

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        var baseUrl = _configuration["PublicAssets:BaseUrl"]
            ?? "https://res.cloudinary.com/do4qn1p2e/image/upload";

        imageUrl = imageUrl.TrimStart('/');

        if (baseUrl.EndsWith('/'))
        {
            return baseUrl + imageUrl;
        }

        return $"{baseUrl}/{imageUrl}";
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

    private string BuildFulfillmentHtml(FulfillmentEmailRequest r)
    {
        var logoUrl = !string.IsNullOrWhiteSpace(r.LogoUrl)
            ? System.Net.WebUtility.HtmlEncode(r.LogoUrl)
            : BuildLogoUrl();

        var logoSection = !string.IsNullOrWhiteSpace(logoUrl)
            ? $"""
              <img
                src="{logoUrl}"
                alt="HotWaterGas"
                width="40"
                height="40"
                style="display:inline-block;vertical-align:middle;margin-right:10px;border-radius:6px;"
              />
              """
            : string.Empty;

        var localizedStatus = LocalizePaymentStatus(r.PaymentStatus);
        var productRows = string.Join("\n", r.Items.Select(BuildFulfillmentItemHtml));

        return $"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Order #{r.OrderCode} — HotWaterGas</title>
</head>
<body style="margin:0;padding:0;background:#0f1117;font-family:Arial,Helvetica,sans-serif;color:#e5e7eb;">
  <table width="100%" cellpadding="0" cellspacing="0" style="background:#0f1117;padding:40px 16px;">
    <tr>
      <td align="center">
        <table width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;">

          <!-- ── HEADER ── -->
          <tr>
            <td style="background:#111827;border-radius:12px 12px 0 0;padding:32px 32px 24px;border:1px solid #1f2937;border-bottom:none;">
              <table width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td>
                    <h1 style="margin:0;font-size:28px;font-weight:800;color:#f9fafb;letter-spacing:-0.5px;line-height:40px;">
                      {logoSection}HotWater<span style="color:#EF4444;">Gas</span>
                    </h1>
                  </td>
                  <td align="right" style="vertical-align:middle;">
                    <span style="display:inline-block;background:#16a34a;color:#ffffff;font-size:12px;font-weight:700;padding:4px 12px;border-radius:9999px;letter-spacing:0.5px;text-transform:uppercase;">
                      Thanh toán thành công
                    </span>
                  </td>
                </tr>
              </table>
              <h2 style="margin:20px 0 0;font-size:20px;font-weight:600;color:#f9fafb;">
                Cảm ơn bạn đã mua hàng!
              </h2>
              <p style="margin:8px 0 0;font-size:14px;color:#9ca3af;line-height:1.6;">
                Đơn hàng của bạn đã được xác nhận và đã hoàn tất thanh toán.
              </p>
            </td>
          </tr>

          <!-- ── ORDER DETAILS ── -->
          <tr>
            <td style="background:#161b22;border:1px solid #1f2937;border-top:none;border-bottom:none;padding:0 32px;">
              <table width="100%" cellpadding="0" cellspacing="0" style="padding:24px 0;border-bottom:1px solid #1f2937;">
                <tr>
                  <td width="50%">
                    <p style="margin:0 0 4px;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:0.8px;">Mã đơn hàng</p>
                    <p style="margin:0;font-size:16px;font-weight:700;color:#f9fafb;">#{r.OrderCode}</p>
                  </td>
                  <td width="50%" align="right">
                    <p style="margin:0 0 4px;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:0.8px;">Ngày đặt hàng</p>
                    <p style="margin:0;font-size:14px;color:#e5e7eb;">{r.OrderDate:dd/MM/yyyy HH:mm} UTC</p>
                  </td>
                </tr>
              </table>
              <table width="100%" cellpadding="0" cellspacing="0" style="padding:24px 0;">
                <tr>
                  <td width="50%">
                    <p style="margin:0 0 4px;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:0.8px;">Trạng thái thanh toán</p>
                    <p style="margin:0;font-size:14px;color:#16a34a;font-weight:600;">{localizedStatus}</p>
                  </td>
                  <td width="50%" align="right">
                    <p style="margin:0 0 4px;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:0.8px;">Tổng cộng</p>
                    <p style="margin:0;font-size:20px;font-weight:800;color:#f9fafb;">{r.FinalTotal:N0} VND</p>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- ── PRODUCT SECTIONS ── -->
          {productRows}

          <!-- ── FOOTER ── -->
          <tr>
            <td style="background:#161b22;border-radius:0 0 12px 12px;border:1px solid #1f2937;border-top:none;padding:28px 32px;">
              <div style="background:#1f2937;border-radius:8px;padding:16px;margin:0 0 20px;border:1px solid #374151;">
                <p style="margin:0;font-size:14px;font-weight:700;color:#fbbf24;text-transform:uppercase;letter-spacing:0.5px;">
                  &#9888;&#65039; Cảnh báo bảo mật
                </p>
                <p style="margin:8px 0 0;font-size:13px;color:#d1d5db;line-height:1.6;">
                  Không chia sẻ khóa Steam của bạn với bất kỳ ai. HotWaterGas không bao giờ yêu cầu bạn cung cấp khóa qua email hoặc chat.
                </p>
              </div>
              <p style="margin:0 0 6px;font-size:13px;color:#6b7280;text-align:center;">
                Nếu bạn cần hỗ trợ, vui lòng liên hệ: <a href="mailto:support@hotwatergas.com" style="color:#3b82f6;text-decoration:none;">support@hotwatergas.com</a>
              </p>
              <p style="margin:16px 0 0;font-size:11px;color:#4b5563;text-align:center;line-height:1.6;">
                HotWaterGas &mdash; Kho game Steam giá rẻ<br>
                Email này được gửi từ hệ thống tự động. Vui lòng không reply email này.
              </p>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    private static string BuildFulfillmentItemHtml(FulfillmentOrderItem item)
    {
        var keyBoxes = string.Join("\n", item.SteamKeys.Select(key => $"""
          <tr>
            <td style="padding:4px 0;">
              <div style="background:#0d1117;border:1px solid #30363d;border-radius:6px;padding:10px 14px;font-family:'Courier New',Courier,monospace;font-size:13px;color:#58a6ff;letter-spacing:1px;word-break:break-all;user-select:all;-webkit-user-select:all;">{System.Net.WebUtility.HtmlEncode(key)}</div>
            </td>
          </tr>
          """));

        var keysSection = item.SteamKeys.Count > 0 ? $"""
          <tr>
            <td style="padding:16px 0 4px;">
              <p style="margin:0;font-size:11px;color:#6b7280;text-transform:uppercase;letter-spacing:0.8px;">Khóa Steam ({item.SteamKeys.Count})</p>
            </td>
          </tr>
          {keyBoxes}
          """ : string.Empty;

        var imageSrc = System.Net.WebUtility.HtmlEncode(item.ProductImageUrl ?? string.Empty);
        var productName = System.Net.WebUtility.HtmlEncode(item.ProductName);
        var quantity = item.Quantity;
        var unitPrice = item.UnitPrice;
        var lineTotal = item.LineTotal;

        return $"""
          <tr>
            <td style="background:#161b22;border:1px solid #1f2937;border-top:none;padding:24px 32px;">
              <table width="100%" cellpadding="0" cellspacing="0">
                <tr>
                  <td width="72" style="vertical-align:top;padding-right:16px;">
                    <img
                      src="{imageSrc}"
                      alt="{productName}"
                      width="72"
                      height="72"
                      style="display:block;border-radius:8px;object-fit:cover;border:1px solid #1f2937;"
                    />
                  </td>
                  <td style="vertical-align:top;">
                    <p style="margin:0 0 4px;font-size:15px;font-weight:700;color:#f9fafb;">{productName}</p>
                    <p style="margin:0 0 6px;font-size:13px;color:#9ca3af;">
                      {quantity} x {unitPrice:N0} VND
                    </p>
                    <p style="margin:0;font-size:14px;font-weight:700;color:#3b82f6;">
                      {lineTotal:N0} VND
                    </p>
                  </td>
                </tr>
              </table>
              <table width="100%" cellpadding="0" cellspacing="0">{keysSection}
              </table>
            </td>
          </tr>
          """;
    }

    private static string BuildFulfillmentText(FulfillmentEmailRequest r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Đơn hàng #{r.OrderCode} — HotWaterGas");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine($"Ngày đặt: {r.OrderDate:dd/MM/yyyy HH:mm} UTC");
        sb.AppendLine($"Trạng thái: {r.PaymentStatus}");
        sb.AppendLine($"Tổng cộng: {r.FinalTotal:N0} VND");
        sb.AppendLine();
        sb.AppendLine("Sản phẩm:");
        sb.AppendLine("---------");
        foreach (var item in r.Items)
        {
            sb.AppendLine($"  {item.ProductName}");
            sb.AppendLine($"    Số lượng: {item.Quantity}  Đơn giá: {item.UnitPrice:N0} VND  Thành tiền: {item.LineTotal:N0} VND");
            if (item.SteamKeys.Count > 0)
            {
                sb.AppendLine($"    Khóa Steam ({item.SteamKeys.Count}):");
                foreach (var key in item.SteamKeys)
                {
                    sb.AppendLine($"      {key}");
                }
            }
            sb.AppendLine();
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("CẢNH BÁO: Không chia sẻ khóa Steam của bạn với bất kỳ ai.");
        sb.AppendLine("Hỗ trợ: support@hotwatergas.com");
        return sb.ToString();
    }
}
