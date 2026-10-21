using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    /// <summary>Saang module gagamitin ang category.</summary>
    public enum CategoryType
    {
        RawMaterial = 1,
        Product = 2
    }

    public class Category
    {
        [Key]
        public int CategoryId { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        // Naka-store bilang string sa DB ("RawMaterial" / "Product") - tingnan ang DbContext
        public CategoryType Type { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<RawMaterial> RawMaterials { get; set; } = new List<RawMaterial>();
    }
}