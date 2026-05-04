using System.Collections.Generic;

namespace Infoman.Models
{
    public class RecipeSaveModel
    {
        public int ProductSizeId { get; set; }
        public decimal Margin { get; set; }
        public List<IngredientModel> Ingredients { get; set; } = new List<IngredientModel>();
    }

    public class IngredientModel
    {
        public int RawMaterialId { get; set; }
        public decimal Quantity { get; set; }
        public string UnitUsed { get; set; } = "g";
    }
}