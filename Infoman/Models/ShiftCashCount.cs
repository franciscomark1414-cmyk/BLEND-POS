using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class ShiftCashCount
    {
        public int ShiftCashCountId { get; set; }

        public int ShiftId { get; set; }
        public Shift Shift { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Denomination { get; set; }

        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }
    }
}