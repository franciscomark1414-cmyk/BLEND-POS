using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class ProductViewModel
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Product name is required")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "SKU is required")]
        [StringLength(50)]
        public string SKU { get; set; } = string.Empty;
        [StringLength(100)]

        public string Category { get; set; } = string.Empty;
        [StringLength(200)]

        public string Description { get; set; } = string.Empty;
        [StringLength(500)]

        public string ImagePath { get; set; } = string.Empty;
        [StringLength(50)]

        public string SelectedProfitMargin { get; set; } = "30%";

        public List<RawMaterial> RawMaterials { get; set; } = new List<RawMaterial>();

        public List<ProductSizeViewModel> Sizes { get; set; } = new List<ProductSizeViewModel>();
    }
}