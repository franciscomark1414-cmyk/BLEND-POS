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
        [MaxLength(20)]
        public string StatusName { get; set; }

        [MaxLength(200)]
        public string? Description { get; set; }
    }
}