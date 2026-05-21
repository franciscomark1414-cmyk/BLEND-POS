namespace Infoman.Models
{
    public static class AdjustmentTypes
    {
        public const int REFUND = 1;
        public const int EXCHANGE_DIFF_PAYMENT = 2;
        public const int EXCHANGE_DIFF_REFUND = 3;
        public const int STORE_CREDIT_REDEMPTION = 4;
    }

    public static class ItemDisposition
    {
        public const int AVAILABLE = 1;
        public const int VOIDED = 2;
        public const int REFUNDED = 3;
        public const int EXCHANGED = 4;
    }

    public static class AdjustmentStatus
    {
        public const string PENDING = "PENDING";
        public const string APPROVED = "APPROVED";
        public const string REJECTED = "REJECTED";
        public const string COMPLETED = "COMPLETED";
    }

    public static class InventoryConsumptionType
    {
        public const string SALE = "SALE";
        public const string EXCHANGE_RETURN = "EXCHANGE_RETURN";
        public const string EXCHANGE_REPLACEMENT = "EXCHANGE_REPLACEMENT";
        public const string VOID_RETURN = "VOID_RETURN";
        public const string REFUND_RETURN = "REFUND_RETURN";
    }
}