using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required]
        public string OrderNumber { get; set; } = string.Empty;
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public decimal TotalAmount { get; set; }
        [Required]  
        public string Status { get; set; } = string.Empty;
        public virtual ICollection<OrderItem> Items { get; set; }
    }
}
