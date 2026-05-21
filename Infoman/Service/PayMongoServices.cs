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
        // ✅ Use your actual keys from PayMongo Dashboard
        // For TEST MODE (development):
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
        /// <param name="amount">Amount in pesos (e.g., 285.00)</param>
        /// <param name="description">Payment description</param>
        /// <param name="orderNumber">Order number for reference</param>
        /// <returns>Checkout URL for GCash payment</returns>
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

                    // Create payload for checkout session
                    var payload = new
                    {
                        data = new
                        {
                            attributes = new
                            {
                                billing = new
                                {
                                    name = orderNumber,
                                    email = "customer@example.com",
                                    phone = ""
                                },
                                send_email_receipt = false,
                                show_description = true,
                                show_line_items = true,
                                line_items = new[]
                                {
                                    new
                                    {
                                        currency = "PHP",
                                        amount = (int)(amount * 100), // Convert to centavos
                                        description = description,
                                        name = "BLEND POS Order",
                                        quantity = 1
                                    }
                                },
                                payment_method_types = new[]
                                {
                                    "gcash"
                                },
                                success_url = "https://localhost:7024/Payment/Success",
                                cancel_url = "https://localhost:7024/Payment/Cancel",
                                description = $"Payment for Order #{orderNumber}"
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
    }
}