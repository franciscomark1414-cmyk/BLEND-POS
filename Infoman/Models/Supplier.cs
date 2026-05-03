using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("Suppliers")]
    public class Supplier
    {
        [Key]
        public int SupplierId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; }

        [MaxLength(100)]
        public string? ContactPerson { get; set; }

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(100)]
        public string? Email { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual ICollection<Receiving> Receivings { get; set; }

        public Supplier()
        {
            Receivings = new HashSet<Receiving>();
        }
    }
}