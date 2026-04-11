using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class ProductViewModel
    {
        public int ProductId { get; set; }

        [Required(ErrorMessage = "Product name is required")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "SKU is required")]
        public string SKU { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string ImagePath { get; set; } = string.Empty;

        public string SelectedProfitMargin { get; set; } = "30%";

        public List<RawMaterial> RawMaterials { get; set; } = new List<RawMaterial>();

        public List<ProductSizeViewModel> Sizes { get; set; } = new List<ProductSizeViewModel>();
    }
}