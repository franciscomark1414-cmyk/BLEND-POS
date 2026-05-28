using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    [Table("ItemDispositionStatus")]
    public class ItemDispositionStatus
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int DispositionId { get; set; }

        [Required]
        [StringLength(50)]
        public string StatusName { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }
    }
}