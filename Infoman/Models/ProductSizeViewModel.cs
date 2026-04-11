using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class ProductSizeViewModel
    {
        public int ProductSizeId { get; set; }

        [Required(ErrorMessage = "Size Name is required")]
        public string SizeName { get; set; } = string.Empty;

        public decimal Price { get; set; }

        [Required(ErrorMessage = "Stock is required")]
        [Range(0, int.MaxValue, ErrorMessage = "Stock must be 0 or greater")]
        public int Stock { get; set; }

        public List<RecipeItemViewModel> RecipeItems { get; set; } = new List<RecipeItemViewModel>();
    }
}