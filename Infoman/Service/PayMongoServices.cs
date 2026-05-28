using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class PayMongoService
    {
        // ✅ Your Test Keys
        private readonly string _secretKey = "sk_test_V9xHNr1czMbDZazRF55Fzb9a";
        private readonly string _apiBaseUrl = "https://api.paymongo.com/v1";

        public PayMongoService()
        {
        }

        public PayMongoService(string secretKey)
        {
            _secretKey = secretKey;
        }

        /// <summary>
        /// Creates a checkout session for GCash payment
        /// </summary>
        public async Task<(bool success, string checkoutUrl, string errorMessage, string sessionId)> CreateCheckoutSessionAsync(
            decimal amount,
            string description,
            string orderNumber)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    // Set Basic Authentication
                    var authToken = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_secretKey}:")
                    );
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", authToken);

                    // ✅ Payload for checkout session
                    var payload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                amount = (int)(amount * 100), // Convert to centavos
                                currency = "PHP",
                                payment_method_types = new[] { "gcash" },
                                description = $"Payment for Order #{orderNumber}",
                                statement_descriptor = "BLEND POS",
                                send_email_receipt = false,
                                show_description = true,
                                show_line_items = true,
                                line_items = new[]
                                {
                                    new
                                    {
                                        currency = "PHP",
                                        amount = (int)(amount * 100),
                                        description = description,
                                        name = "BLEND POS Order",
                                        quantity = 1
                                    }
                                },
                                success_url = "https://localhost:7024/Payment/Success",
                                cancel_url = "https://localhost:7024/Payment/Cancel"
                            }
                        }
                    };

                    var json = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(
                        $"{_apiBaseUrl}/checkout_sessions",
                        content
                    );

                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string checkoutUrl = result.data.attributes.checkout_url;
                        string sessionId = result.data.id;

                        return (true, checkoutUrl, null, sessionId);
                    }
                    else
                    {
                        return (false, null, $"PayMongo API Error: {responseString}", null);
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Retrieves checkout session status
        /// </summary>
        public async Task<(bool success, string status, string errorMessage)> GetCheckoutSessionStatusAsync(string sessionId)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var authToken = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_secretKey}:")
                    );
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", authToken);

                    var response = await client.GetAsync(
                        $"{_apiBaseUrl}/checkout_sessions/{sessionId}"
                    );

                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string status = result.data.attributes.status;

                        return (true, status, null);
                    }
                    else
                    {
                        return (false, null, $"Error getting status: {responseString}");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a GCash Source (for test mode - no need to enable GCash in dashboard)
        /// </summary>
        public async Task<(bool success, string checkoutUrl, string errorMessage, string sourceId)> CreateGCashSourceAsync(
    decimal amount,
    string orderNumber,
    string successUrl,
    string cancelUrl)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var authToken = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_secretKey}:")
                    );
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", authToken);

                    // ✅ GAMITIN ANG SIMPLENG URLS (walang /{id})
                    var payload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                amount = (int)(amount * 100),
                                currency = "PHP",
                                type = "gcash",
                                redirect = new
                                {
                                    success = successUrl,   // ✅ Simple URL lang
                                    failed = cancelUrl     // ✅ Simple URL lang
                                },
                                billing = new
                                {
                                    name = $"Order #{orderNumber}",
                                    email = "customer@example.com"
                                }
                            }
                        }
                    };

                    var json = JsonConvert.SerializeObject(payload);
                    System.Diagnostics.Debug.WriteLine($"Request Payload: {json}");

                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync(
                        $"{_apiBaseUrl}/sources",
                        content
                    );

                    var responseString = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Response: {responseString}");

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string checkoutUrl = result.data.attributes.redirect.checkout_url;
                        string sourceId = result.data.id;

                        System.Diagnostics.Debug.WriteLine($"✅ GCash Source Created - SourceId: {sourceId}");
                        System.Diagnostics.Debug.WriteLine($"✅ Checkout URL: {checkoutUrl}");

                        return (true, checkoutUrl, null, sourceId);
                    }
                    else
                    {
                        return (false, null, $"PayMongo API Error: {responseString}", null);
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Retrieves Source status
        /// </summary>
        public async Task<(bool success, string status, string errorMessage)> GetSourceStatusAsync(string sourceId)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var authToken = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_secretKey}:")
                    );
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", authToken);

                    var response = await client.GetAsync(
                        $"{_apiBaseUrl}/sources/{sourceId}"
                    );

                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string status = result.data.attributes.status;

                        return (true, status, null);
                    }
                    else
                    {
                        return (false, null, $"Error: {responseString}");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a QRPh payment intent for POS (customer scans with their phone)
        /// </summary>
        public async Task<(bool success, string qrCodeBase64, string errorMessage, string paymentIntentId)> CreateQRPhPaymentIntentAsync(
     decimal amount,
     string orderNumber)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var authToken = Convert.ToBase64String(
                        Encoding.ASCII.GetBytes($"{_secretKey}:")
                    );
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", authToken);

                    // ========== STEP 1: CREATE PAYMENT INTENT ==========
                    var intentPayload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                amount = (int)(amount * 100),
                                currency = "PHP",
                                payment_method_allowed = new[] { "qrph" },  // ✅ QRPH
                                description = $"Payment for Order #{orderNumber}",
                                capture_type = "automatic"
                            }
                        }
                    };

                    var intentJson = JsonConvert.SerializeObject(intentPayload);
                    var intentContent = new StringContent(intentJson, Encoding.UTF8, "application/json");

                    var intentResponse = await client.PostAsync(
                        $"{_apiBaseUrl}/payment_intents",
                        intentContent
                    );

                    var intentString = await intentResponse.Content.ReadAsStringAsync();

                    if (!intentResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Payment Intent Error: {intentString}", null);
                    }

                    dynamic intentResult = JsonConvert.DeserializeObject(intentString);
                    string paymentIntentId = intentResult.data.id;

                    // ========== STEP 2: CREATE PAYMENT METHOD (QRPH) ==========
                    var methodPayload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                type = "qrph",  // ✅ QRPH
                                billing = new
                                {
                                    name = $"Order #{orderNumber}",
                                    email = "customer@example.com"
                                }
                            }
                        }
                    };

                    var methodJson = JsonConvert.SerializeObject(methodPayload);
                    var methodContent = new StringContent(methodJson, Encoding.UTF8, "application/json");

                    var methodResponse = await client.PostAsync(
                        $"{_apiBaseUrl}/payment_methods",
                        methodContent
                    );

                    var methodString = await methodResponse.Content.ReadAsStringAsync();

                    if (!methodResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Payment Method Error: {methodString}", null);
                    }

                    dynamic methodResult = JsonConvert.DeserializeObject(methodString);
                    string paymentMethodId = methodResult.data.id;

                    // ========== STEP 3: ATTACH ==========
                    var attachPayload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                payment_method = paymentMethodId
                            }
                        }
                    };

                    var attachJson = JsonConvert.SerializeObject(attachPayload);
                    var attachContent = new StringContent(attachJson, Encoding.UTF8, "application/json");

                    var attachResponse = await client.PostAsync(
                        $"{_apiBaseUrl}/payment_intents/{paymentIntentId}/attach",
                        attachContent
                    );

                    var attachString = await attachResponse.Content.ReadAsStringAsync();

                    if (!attachResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Attach Error: {attachString}", null);
                    }

                    dynamic attachResult = JsonConvert.DeserializeObject(attachString);

                    // ========== STEP 4: GET QR CODE ==========
                    string qrCodeBase64 = null;

                    try
                    {
                        if (attachResult.data.attributes.next_action != null)
                        {
                            var nextAction = attachResult.data.attributes.next_action;

                            // ✅ PARA SA QRPH: next_action.type ay "qr_code"
                            if (nextAction.type == "qr_code" && nextAction.qr_code != null)
                            {
                                qrCodeBase64 = nextAction.qr_code;
                                System.Diagnostics.Debug.WriteLine("QR Code found in next_action.qr_code");
                            }
                            // Fallback para sa code
                            else if (nextAction.code != null && nextAction.code.image_url != null)
                            {
                                qrCodeBase64 = nextAction.code.image_url;
                                System.Diagnostics.Debug.WriteLine("QR Code found in next_action.code.image_url");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting QR code: {ex.Message}");
                    }

                    if (string.IsNullOrEmpty(qrCodeBase64))
                    {
                        return (false, null, "No QR code in response. Full response: " + attachString, paymentIntentId);
                    }

                    return (true, qrCodeBase64, null, paymentIntentId);
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}", null);
            }
        }


    }
}