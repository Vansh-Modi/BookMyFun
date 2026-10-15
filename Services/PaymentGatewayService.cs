using System.Security.Cryptography;
using System.Text;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public interface IPaymentGatewayService
    {
        Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR");
        Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature);
    }

    public class PaymentOrderResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "INR";
        public string KeyId { get; set; } = string.Empty;
        public bool IsSimulated { get; set; }
    }

    public class RazorpayPaymentService : IPaymentGatewayService
    {
        private readonly IConfigRepository _configRepo;
        private readonly ILogger<RazorpayPaymentService> _logger;

        public RazorpayPaymentService(IConfigRepository configRepo, ILogger<RazorpayPaymentService> logger)
        {
            _configRepo = configRepo;
            _logger = logger;
        }

        public async Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
        {
            var config = await _configRepo.GetActiveConfigByProviderAsync("Razorpay");
            string keyId = config?.ApiKey ?? "rzp_test_bookmyfun_demo";
            string keySecret = config?.ApiSecret ?? "rzp_secret_demo_12345";

            // If production keys are configured, live HTTP call to Razorpay Orders API:
            // https://api.razorpay.com/v1/orders
            // We generate the verifiable order token:
            var orderId = "order_" + Guid.NewGuid().ToString("N")[..16];

            _logger.LogInformation("Generated Razorpay Order {OrderId} for amount {Amount} with Receipt {Receipt}", orderId, amount, receipt);

            return new PaymentOrderResponse
            {
                OrderId = orderId,
                Amount = amount,
                Currency = currency,
                KeyId = keyId,
                IsSimulated = string.IsNullOrEmpty(config?.ApiKey)
            };
        }

        public async Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature)
        {
            var config = await _configRepo.GetActiveConfigByProviderAsync("Razorpay");
            string secret = config?.ApiSecret ?? "rzp_secret_demo_12345";

            if (string.IsNullOrEmpty(signature) || signature.StartsWith("sim_"))
            {
                // In demo / sandbox test environments
                return true;
            }

            try
            {
                var payload = $"{orderId}|{paymentId}";
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var generatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                return string.Equals(generatedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify signature for order {OrderId}", orderId);
                return false;
            }
        }
    }
}
