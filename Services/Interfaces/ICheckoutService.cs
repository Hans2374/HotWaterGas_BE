using Services.DTOs;

namespace Services.Interfaces;

public interface ICheckoutService
{
    Task<CheckoutPreviewResponse> PreviewCheckoutAsync(List<Guid> cartItemIds, CancellationToken cancellationToken = default);
    Task<CreatePaymentResponse> CreatePaymentAsync(List<Guid> selectedCartItemIds, CancellationToken cancellationToken = default);
    Task<PaymentReturnResponse> ProcessPaymentReturnAsync(string orderCode, string status, bool success, string? transactionId, decimal? amountPaid, CancellationToken cancellationToken = default);
}
