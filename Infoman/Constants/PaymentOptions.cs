namespace Infoman.Constants
{
    // PHASE 1: Feature flags para sa payment methods.
    // Naka-fail-closed: kapag walang nakalagay sa appsettings, naka-OFF ang GCash at QRPh.
    // Ang Cash ay laging available.
    public class PaymentOptions
    {
        public const string SectionName = "Payments";

        public bool EnableGCash { get; set; } = false;
        public bool EnableQRPh { get; set; } = false;

        public bool IsEnabled(string? paymentMethod)
        {
            return paymentMethod switch
            {
                "Cash" => true,
                "GCash" => EnableGCash,
                "QRPh" => EnableQRPh,
                _ => false
            };
        }
    }
}