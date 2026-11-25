using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class PayMongoService
    {
        // TIP: ilipat mo ito sa appsettings.json / User Secrets / environment variable.
        private readonly string _secretKey = "sk_test_ktWDbcAYijTdB2erdvTmzs3H";
        private readonly string _apiBaseUrl = "https://api.paymongo.com/v1";

        public PayMongoService()
        {
            _secretKey = Environment.GetEnvironmentVariable("PAYMONGO_SECRET_KEY");
        }

        public PayMongoService(string secretKey)
        {
            _secretKey = secretKey;
        }

        /// <summary>
        /// True kapag test key (sk_test_...) ang gamit. Gamitin ito sa UI para
        /// ipakita ang "Simulate payment (TEST)" button.
        /// </summary>
        public bool IsTestMode => _secretKey != null && _secretKey.StartsWith("sk_test_");

        private HttpClient CreateClient()
        {

            if (string.IsNullOrWhiteSpace(_secretKey))
            {
                throw new InvalidOperationException(
                    "PayMongo secret key is not configured."
                );
            }
            var client = new HttpClient();
            var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_secretKey}:"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", authToken);
            return client;
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
                using (var client = CreateClient())
                {
                    var payload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                amount = (int)(amount * 100),
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

                    var response = await client.PostAsync($"{_apiBaseUrl}/checkout_sessions", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string checkoutUrl = result.data.attributes.checkout_url;
                        string sessionId = result.data.id;

                        return (true, checkoutUrl, null, sessionId);
                    }

                    return (false, null, $"PayMongo API Error: {responseString}", null);
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
                using (var client = CreateClient())
                {
                    var response = await client.GetAsync($"{_apiBaseUrl}/checkout_sessions/{sessionId}");
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string status = result.data.attributes.status;
                        return (true, status, null);
                    }

                    return (false, null, $"Error getting status: {responseString}");
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a GCash Source. Sa test mode, may PayMongo test page (Authorize / Fail),
        /// kaya ligtas ito i-test nang walang totoong pera.
        /// </summary>
        public async Task<(bool success, string checkoutUrl, string errorMessage, string sourceId)> CreateGCashSourceAsync(
            decimal amount,
            string orderNumber,
            string successUrl,
            string cancelUrl)
        {
            try
            {
                using (var client = CreateClient())
                {
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
                                    success = successUrl,
                                    failed = cancelUrl
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
                    var response = await client.PostAsync($"{_apiBaseUrl}/sources", content);
                    var responseString = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Response: {responseString}");

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string checkoutUrl = result.data.attributes.redirect.checkout_url;
                        string sourceId = result.data.id;

                        return (true, checkoutUrl, null, sourceId);
                    }

                    return (false, null, $"PayMongo API Error: {responseString}", null);
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
                using (var client = CreateClient())
                {
                    var response = await client.GetAsync($"{_apiBaseUrl}/sources/{sourceId}");
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        dynamic result = JsonConvert.DeserializeObject(responseString);
                        string status = result.data.attributes.status;
                        return (true, status, null);
                    }

                    return (false, null, $"Error: {responseString}");
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// BACKWARD-COMPATIBLE: parehong signature ng dati, kaya hindi masisira ang controller mo.
        /// Kung kailangan mo ang test_url, gamitin ang CreateQRPhPaymentIntentWithTestUrlAsync.
        /// </summary>
        public async Task<(bool success, string qrCodeBase64, string errorMessage, string paymentIntentId)> CreateQRPhPaymentIntentAsync(
            decimal amount,
            string orderNumber)
        {
            var r = await CreateQRPhPaymentIntentWithTestUrlAsync(amount, orderNumber);
            return (r.success, r.qrCodeBase64, r.errorMessage, r.paymentIntentId);
        }

        /// <summary>
        /// Creates a QR Ph payment intent. Ibinabalik din ang testUrl (test mode lang)
        /// para ma-simulate ang bayad nang HINDI nag-i-scan gamit ang totoong GCash app.
        /// </summary>
        public async Task<(bool success, string qrCodeBase64, string errorMessage, string paymentIntentId, string testUrl)> CreateQRPhPaymentIntentWithTestUrlAsync(
            decimal amount,
            string orderNumber)
        {
            try
            {
                using (var client = CreateClient())
                {
                    // ========== STEP 1: CREATE PAYMENT INTENT ==========
                    var intentPayload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                amount = (int)(amount * 100),
                                currency = "PHP",
                                payment_method_allowed = new[] { "qrph" },
                                description = $"Payment for Order #{orderNumber}",
                                capture_type = "automatic"
                            }
                        }
                    };

                    var intentContent = new StringContent(
                        JsonConvert.SerializeObject(intentPayload), Encoding.UTF8, "application/json");

                    var intentResponse = await client.PostAsync($"{_apiBaseUrl}/payment_intents", intentContent);
                    var intentString = await intentResponse.Content.ReadAsStringAsync();

                    if (!intentResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Payment Intent Error: {intentString}", null, null);
                    }

                    string paymentIntentId = (string)JObject.Parse(intentString)["data"]["id"];

                    // ========== STEP 2: CREATE PAYMENT METHOD (QRPH) ==========
                    var methodPayload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                type = "qrph",
                                billing = new
                                {
                                    name = $"Order #{orderNumber}",
                                    email = "customer@example.com"
                                }
                            }
                        }
                    };

                    var methodContent = new StringContent(
                        JsonConvert.SerializeObject(methodPayload), Encoding.UTF8, "application/json");

                    var methodResponse = await client.PostAsync($"{_apiBaseUrl}/payment_methods", methodContent);
                    var methodString = await methodResponse.Content.ReadAsStringAsync();

                    if (!methodResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Payment Method Error: {methodString}", null, null);
                    }

                    string paymentMethodId = (string)JObject.Parse(methodString)["data"]["id"];

                    // ========== STEP 3: ATTACH PAYMENT METHOD ==========
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

                    var attachContent = new StringContent(
                        JsonConvert.SerializeObject(attachPayload), Encoding.UTF8, "application/json");

                    var attachResponse = await client.PostAsync(
                        $"{_apiBaseUrl}/payment_intents/{paymentIntentId}/attach", attachContent);
                    var attachString = await attachResponse.Content.ReadAsStringAsync();

                    if (!attachResponse.IsSuccessStatusCode)
                    {
                        return (false, null, $"Attach Error: {attachString}", null, null);
                    }

                    // Log ang buong response para makita mo kung nasaan ang test_url
                    System.Diagnostics.Debug.WriteLine($"QRPh attach response: {attachString}");

                    // ========== STEP 4: GET QR CODE + TEST URL ==========
                    string qrCodeBase64 = null;
                    string testUrl = null;

                    try
                    {
                        var root = JObject.Parse(attachString);

                        qrCodeBase64 = (string)root.SelectToken("data.attributes.next_action.code.image_url");

                        // Recursive search, kaya gagana kahit nasaan sa next_action ang test_url
                        testUrl = (string)root.SelectToken("$..test_url");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error extracting QR code: {ex.Message}");
                    }

                    if (string.IsNullOrEmpty(qrCodeBase64))
                    {
                        return (false, null, "No QR code in response. Full response: " + attachString, paymentIntentId, null);
                    }

                    // Ibalik lang ang testUrl kapag test mode (safety)
                    if (!IsTestMode)
                    {
                        testUrl = null;
                    }

                    return (true, qrCodeBase64, null, paymentIntentId, testUrl);
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Retrieves Payment Intent status (para sa polling ng QR Ph).
        /// Mga status: awaiting_payment_method, awaiting_next_action, processing, succeeded
        /// </summary>
        public async Task<(bool success, string status, string errorMessage)> GetPaymentIntentStatusAsync(string paymentIntentId)
        {
            try
            {
                using (var client = CreateClient())
                {
                    var response = await client.GetAsync($"{_apiBaseUrl}/payment_intents/{paymentIntentId}");
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        string status = (string)JObject.Parse(responseString).SelectToken("data.attributes.status");
                        return (true, status, null);
                    }

                    return (false, null, $"Error: {responseString}");
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }
    }
}
