using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class DashboardViewModel
    {
        public List<BestSellingProductVM> BestSellingProducts { get; set; } = new();
        public List<ProductViewModel> Products { get; set; } = new();
    }


    public class BestSellingProductVM
    {
        public int Rank { get; set; }
        [StringLength(100)]
        public string Name { get; set; }
        [StringLength(50)]
        public string SizeName { get; set; }
        public decimal Price { get; set; }
        public int TotalSold { get; set; }
        [StringLength(500)]
        public string ImagePath { get; set; }
    }
}

