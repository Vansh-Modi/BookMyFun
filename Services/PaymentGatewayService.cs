using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookMyFun.Models.Entities;
using BookMyFun.Repositories;

namespace BookMyFun.Services
{
    public interface IPaymentGatewayService
    {
        Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR");
        Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature);
        Task<PaymentRefundResult> ProcessRefundAsync(string paymentId, decimal amount, string? reason = null)
            => Task.FromResult(new PaymentRefundResult { Success = true, RefundId = $"rfnd_sim_{Guid.NewGuid():N}"[..20], IsPending = false });
    }

    public class PaymentRefundResult
    {
        public bool Success { get; set; }
        public string? RefundId { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsPending { get; set; }
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
        private readonly IConfiguration _configuration;
        private readonly ILogger<RazorpayPaymentService> _logger;
        private readonly IHttpClientFactory? _httpClientFactory;

        public RazorpayPaymentService(
            IConfigRepository configRepo, 
            IConfiguration configuration,
            ILogger<RazorpayPaymentService> logger,
            IHttpClientFactory? httpClientFactory = null)
        {
            _configRepo = configRepo;
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<PaymentOrderResponse> CreateOrderAsync(decimal amount, string receipt, string currency = "INR")
        {
            var (keyId, keySecret) = await GetCredentialsAsync();
            bool isDemoKey = keyId.Contains("demo") || keySecret.Contains("demo");

            // If real live or valid Razorpay test key is provided, invoke Razorpay Orders REST API
            if (!isDemoKey && !string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(keySecret))
            {
                try
                {
                    var http = _httpClientFactory != null 
                        ? _httpClientFactory.CreateClient("Razorpay") 
                        : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var authBytes = Encoding.UTF8.GetBytes($"{keyId.Trim()}:{keySecret.Trim()}");
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                    var orderPayload = new
                    {
                        amount = (long)Math.Round(amount * 100m, 0), // Razorpay accepts subunits (paise)
                        currency = currency,
                        receipt = receipt,
                        payment_capture = 1,
                        notes = new Dictionary<string, string>
                        {
                            { "platform", "BookMyFun" },
                            { "receipt", receipt }
                        }
                    };

                    var content = new StringContent(JsonSerializer.Serialize(orderPayload), Encoding.UTF8, "application/json");
                    var response = await http.PostAsync("https://api.razorpay.com/v1/orders", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(jsonString);
                        if (doc.RootElement.TryGetProperty("id", out var idProp))
                        {
                            var realOrderId = idProp.GetString();
                            if (!string.IsNullOrWhiteSpace(realOrderId))
                            {
                                _logger.LogInformation("Successfully created live Razorpay Order {OrderId} for amount {Amount} INR", realOrderId, amount);
                                return new PaymentOrderResponse
                                {
                                    OrderId = realOrderId,
                                    Amount = amount,
                                    Currency = currency,
                                    KeyId = keyId.Trim(),
                                    IsSimulated = false
                                };
                            }
                        }
                    }
                    else
                    {
                        var errText = await response.Content.ReadAsStringAsync();
                        _logger.LogWarning("Razorpay API order creation failed with status {Status}: {Err}. Falling back to test order.", response.StatusCode, errText);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception calling Razorpay Orders API. Falling back to test order token.");
                }
            }

            // Fallback: Generate valid verifiable simulated order token for sandbox / demo
            var simulatedOrderId = "order_" + Guid.NewGuid().ToString("N")[..16];
            _logger.LogInformation("Generated Razorpay Order token {OrderId} for amount {Amount} with Receipt {Receipt}", simulatedOrderId, amount, receipt);

            return new PaymentOrderResponse
            {
                OrderId = simulatedOrderId,
                Amount = amount,
                Currency = currency,
                KeyId = keyId,
                IsSimulated = true
            };
        }

        public async Task<bool> VerifyPaymentSignatureAsync(string orderId, string paymentId, string signature)
        {
            if (string.IsNullOrWhiteSpace(orderId) || 
                string.IsNullOrWhiteSpace(paymentId) || 
                string.IsNullOrWhiteSpace(signature))
            {
                return false;
            }

            var config = await _configRepo.GetActiveConfigByProviderAsync("Razorpay");
            string? secret = config?.ApiSecret;

            if (string.IsNullOrWhiteSpace(secret))
            {
                secret = _configuration["Razorpay:KeySecret"];
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("Razorpay API secret is not configured. Cannot verify payment signature.");
                return false;
            }

            try
            {
                var payload = $"{orderId}|{paymentId}";
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret.Trim()));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var generatedSignature = Convert.ToHexString(hash).ToLowerInvariant();

                byte[] generatedBytes = Encoding.UTF8.GetBytes(generatedSignature);
                byte[] providedBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

                return CryptographicOperations.FixedTimeEquals(generatedBytes, providedBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify signature for order {OrderId}", orderId);
                return false;
            }
        }

        private async Task<(string keyId, string keySecret)> GetCredentialsAsync()
        {
            var config = await _configRepo.GetActiveConfigByProviderAsync("Razorpay");
            string keyId = config?.ApiKey ?? string.Empty;
            string keySecret = config?.ApiSecret ?? string.Empty;

            if (string.IsNullOrWhiteSpace(keyId))
            {
                keyId = _configuration["Razorpay:KeyId"] ?? string.Empty;
                keySecret = _configuration["Razorpay:KeySecret"] ?? string.Empty;
            }
            return (keyId, keySecret);
        }

        public async Task<PaymentRefundResult> ProcessRefundAsync(string paymentId, decimal amount, string? reason = null)
        {
            if (amount <= 0)
            {
                return new PaymentRefundResult { Success = true, RefundId = null, IsPending = false };
            }

            var (keyId, keySecret) = await GetCredentialsAsync();

            // Handle simulated payment IDs or test mode without real secrets
            if (paymentId.StartsWith("sim_") || paymentId.StartsWith("PAY_") || paymentId.StartsWith("GATEWAY_") ||
                string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret) ||
                keyId.Contains("YOUR_KEY") || keyId.Contains("TEST_KEY"))
            {
                _logger.LogInformation("Simulating gateway refund of ₹{Amount:N2} for transaction {PaymentId}", amount, paymentId);
                return new PaymentRefundResult
                {
                    Success = true,
                    RefundId = $"rfnd_sim_{Guid.NewGuid():N}"[..20],
                    IsPending = false
                };
            }

            try
            {
                var http = _httpClientFactory != null
                    ? _httpClientFactory.CreateClient("Razorpay")
                    : new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

                var authBytes = Encoding.UTF8.GetBytes($"{keyId.Trim()}:{keySecret.Trim()}");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

                long amountInPaise = (long)Math.Round(amount * 100m);
                var payloadObj = new
                {
                    amount = amountInPaise,
                    notes = new Dictionary<string, string>
                    {
                        { "reason", reason ?? "Customer cancellation" }
                    }
                };

                string cleanPaymentId = paymentId.StartsWith("RAZORPAY_") ? paymentId.Substring(9) : paymentId;
                var content = new StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");
                var response = await http.PostAsync($"https://api.razorpay.com/v1/payments/{cleanPaymentId}/refund", content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseJson);
                    string refundId = doc.RootElement.GetProperty("id").GetString() ?? $"rfnd_{Guid.NewGuid():N}"[..20];
                    _logger.LogInformation("Razorpay refund processed successfully: {RefundId} for payment {PaymentId}", refundId, paymentId);
                    return new PaymentRefundResult { Success = true, RefundId = refundId, IsPending = false };
                }

                var errText = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Razorpay refund failed HTTP {StatusCode}: {Error}. Marking as Pending for retry.", response.StatusCode, errText);
                return new PaymentRefundResult { Success = false, IsPending = true, ErrorMessage = errText };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception calling Razorpay refund for payment {PaymentId}. Marking as Pending.", paymentId);
                return new PaymentRefundResult { Success = false, IsPending = true, ErrorMessage = ex.Message };
            }
        }
    }
}
