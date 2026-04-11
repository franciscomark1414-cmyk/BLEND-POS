namespace Infoman.Models
{
    public class DashboardViewModel
    {
        public List<BestSellingProductVM> BestSellingProducts { get; set; } = new();
    }

    public class BestSellingProductVM
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int TotalSold { get; set; }
    }
}
