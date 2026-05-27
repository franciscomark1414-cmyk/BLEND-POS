using System;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public enum RawMaterialCategory
    {
        Fruits,
        Dairy,
        Others,
        Sweeteners,
        Toppings
    }

    public class RawMaterial
    {
        [Key]
        public int RawMaterialId { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public decimal Stock { get; set; }

        [Required]
        public decimal MinQty { get; set; } // minimum quantity para sa low stock alert

        [Required]
        public decimal Price { get; set; }
        public string? SKU { get; set; }

        public decimal MinQtyInBaseUnit { get; set; }  // For internal comparison (g, mL)

        [DataType(DataType.Date)]
        [Display(Name = "Expiration Date")]
        public DateTime? ExpirationDate { get; set; } // nullable kung walang expiration

        [Required]
        public RawMaterialCategory Category { get; set; } // Fruits, Dairy, etc.
        [Required]
        [Display(Name = "Unit")]
        public string Unit { get; set; }
        [Required]
        public string BaseUnit { get; set; } = "g";  // grams, milliliters, pieces

        [Required]
        public decimal ConversionFactor { get; set; } = 1;  // 1 kg = 1000 g → factor = 1000

        // Computed property for total value
        public decimal TotalValue => Stock * Price;

        // Computed property for status
        public string Status
        {
            get
            {
                if (Stock == 0)
                    return "No Stock";

                if (Stock <= MinQty)
                    return "Low Stock";

                if (ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(3))
                    return "Expiring Soon";

                return "In Stock";
            }
        }
        public bool IsDeleted { get; set; } = false;

       
        public virtual ICollection<RawMaterialBatch> Batches { get; set; }

        public RawMaterial()
        {
            Batches = new HashSet<RawMaterialBatch>();
        }

        public string StockWithUnit
        {
            get
            {
                if (Unit == "kg" && BaseUnit == "g")
                {
                    decimal displayStock = Stock / 1000;
                    return $"{displayStock:F2} {Unit}";
                }
                else if (Unit == "L" && BaseUnit == "mL")
                {
                    decimal displayStock = Stock / 1000;
                    return $"{displayStock:F2} {Unit}";
                }
                return $"{Stock:F2} {Unit}";
            }
        }
        public string MinQtyWithUnit
        {
            get
            {
                decimal displayMinQty = MinQty;

                // I-convert kung ang base unit ay g/mL at display unit ay kg/L
                if (BaseUnit == "g" && Unit == "kg")
                {
                    displayMinQty = MinQty / 1000;  // 10,000 g → 10 kg
                }
                else if (BaseUnit == "mL" && Unit == "L")
                {
                    displayMinQty = MinQty / 1000;  // 10,000 mL → 10 L
                }

                return $"{displayMinQty:N1} {Unit}";
            }
        }
    }
}