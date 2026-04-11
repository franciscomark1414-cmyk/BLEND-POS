using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required]
        public DateTime Date { get; set; }
        [Required]
        public decimal TotalAmount { get; set; }
        [Required]  
        public string Status { get; set; }
        public virtual ICollection<OrderItem> Items { get; set; }
    }
}
