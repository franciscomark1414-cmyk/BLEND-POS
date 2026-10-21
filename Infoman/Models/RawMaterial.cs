using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{

    public class RawMaterial
    {
        [Key]
        public int RawMaterialId { get; set; }

        [Required]
        [StringLength(500)]
        public string Name { get; set; }

        [Required]
        public decimal Stock { get; set; }

        [Required]
        public decimal MinQty { get; set; } // minimum quantity para sa low stock alert (DISPLAY UNIT)

        [Required]
        public decimal Price { get; set; }
        [StringLength(50)]
        public string? SKU { get; set; }

        public decimal MinQtyInBaseUnit { get; set; }  // For internal comparison (g, mL)

        [DataType(DataType.Date)]
        [Display(Name = "Expiration Date")]
        public DateTime? ExpirationDate { get; set; }

        public int? CategoryId { get; set; }
        [ForeignKey("CategoryId")]
        public virtual Category? CategoryInfo { get; set; }     // sinadyang HINDI "Category" para mag-error ang lumang code, hindi tahimik na magkamali

        [NotMapped]
        public string? CategoryName { get; set; }                // pang-display lang


        [Required]
        [Display(Name = "Unit")]
        [StringLength(50)]
        public string Unit { get; set; }

        [Required]
        [StringLength(50)]
        public string BaseUnit { get; set; } = "g";

        [Required]
        public decimal ConversionFactor { get; set; } = 1;

        // Computed property for total value
        public decimal TotalValue => Stock * Price;

        public bool IsDeleted { get; set; } = false;

        public virtual ICollection<RawMaterialBatch> Batches { get; set; }

        public RawMaterial()
        {
            Batches = new HashSet<RawMaterialBatch>();
        }

        // ✅ ITO ANG FIX SA STATUS
        // Dapat i-compare ang Stock (base unit) sa MinQtyInBaseUnit
        [StringLength(50)]
        public string Status
        {
            get
            {
                if (Stock <= 0)
                    return "No Stock";

                // ✅ GAMITIN ANG MinQtyInBaseUnit PARA TUGMA SA STOCK
                if (Stock <= MinQtyInBaseUnit)
                    return "Low Stock";

                if (ExpirationDate.HasValue && ExpirationDate.Value <= DateTime.Today.AddDays(3))
                    return "Expiring Soon";

                return "In Stock";
            }
        }

        // ✅ ITO ANG FIX SA STOCKWITHUNIT
        // Wag mag-divide kung ang BaseUnit ay pareho na sa Unit (e.g., g -> g)
        [StringLength(50)]
        public string StockWithUnit
        {
            get
            {
                // ✅ KUNG BASE UNIT AT UNIT AY PAREHO, WAG MAG-DIVIDE
                if (BaseUnit == Unit)
                {
                    return $"{Stock:F2} {Unit}";
                }

                // ✅ KUNG BASE UNIT (g/mL) AT DISPLAY UNIT (kg/L)
                if (BaseUnit == "g" && Unit == "kg")
                {
                    return $"{Stock / 1000:F2} {Unit}";
                }
                if (BaseUnit == "mL" && Unit == "L")
                {
                    return $"{Stock / 1000:F2} {Unit}";
                }

                return $"{Stock:F2} {Unit}";
            }
        }

        // ✅ ITO ANG FIX SA MINQTYWITHUNIT
        // Gamitin ang MinQtyInBaseUnit imbes na MinQty (na naka-display unit na)
        [StringLength(50)]
        public string MinQtyWithUnit
        {
            get
            {
                decimal displayMinQty = MinQtyInBaseUnit;

                // ✅ KUNG BASE UNIT AT UNIT AY PAREHO, WAG MAG-DIVIDE
                if (BaseUnit == Unit)
                {
                    displayMinQty = MinQtyInBaseUnit;
                }
                // ✅ KUNG BASE UNIT (g/mL) AT DISPLAY UNIT (kg/L)
                else if (BaseUnit == "g" && Unit == "kg")
                {
                    displayMinQty = MinQtyInBaseUnit / 1000;
                }
                else if (BaseUnit == "mL" && Unit == "L")
                {
                    displayMinQty = MinQtyInBaseUnit / 1000;
                }

                return $"{displayMinQty:N1} {Unit}";
            }
        }
    }
}