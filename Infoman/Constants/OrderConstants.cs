// Constants/OrderConstants.cs
namespace Infoman.Constants
{
    public static class OrderStatus
    {
        public const string PENDING = "PENDING";
        public const string PREPARING = "PREPARING";
        public const string COMPLETED = "COMPLETED";
        public const string VOIDED = "VOIDED";
        public const string REFUNDED = "REFUNDED";

        public static readonly string[] AllowedStatuses = { PENDING, PREPARING, COMPLETED, VOIDED, REFUNDED };

        public static bool IsValidStatus(string status)
        {
            return AllowedStatuses.Contains(status);
        }
    }

    public static class AdjustmentTypeConstants
    {
        public const int REFUND = 1;
        public const int EXCHANGE_DIFF_PAYMENT = 2;
        public const int EXCHANGE_DIFF_REFUND = 3;
        public const int STORE_CREDIT_REDEMPTION = 4;

        public static bool IsPositiveAdjustment(int adjustmentTypeId)
        {
            return adjustmentTypeId == EXCHANGE_DIFF_PAYMENT || adjustmentTypeId == STORE_CREDIT_REDEMPTION;
        }

        public static bool RequiresApproval(int adjustmentTypeId)
        {
            return adjustmentTypeId == REFUND || adjustmentTypeId == EXCHANGE_DIFF_REFUND;
        }
    }

    public static class ItemDispositionConstants
    {
        public const int AVAILABLE = 1;
        public const int VOIDED = 2;
        public const int REFUNDED = 3;
        public const int EXCHANGED = 4;
    }

    public static class InventoryConsumptionTypeConstants
    {
        public const string SALE = "SALE";
        public const string EXCHANGE_RETURN = "EXCHANGE_RETURN";
        public const string EXCHANGE_REPLACEMENT = "EXCHANGE_REPLACEMENT";
        public const string VOID_RETURN = "VOID_RETURN";
        public const string REFUND_RETURN = "REFUND_RETURN";
    }
}