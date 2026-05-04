using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class RecipeItemViewModel
    {
        [Required(ErrorMessage = "Please select an ingredient")]
        [Range(1, int.MaxValue, ErrorMessage = "Please select an ingredient")]
        public int RawMaterialId { get; set; }

        public string? RawMaterialName { get; set; }

        [Required(ErrorMessage = "Quantity is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        public decimal Quantity { get; set; }

        public string UnitUsed { get; set; } = "g";
        

        public decimal Price { get; set; }
    }
}