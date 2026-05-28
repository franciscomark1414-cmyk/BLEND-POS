using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class EmployeePerformance
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }  // FK to User table

        [ForeignKey("UserId")]
        public virtual User? Employee { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;

        [Range(0, 999999)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalSales { get; set; } = 0;  // Total sales na na-process ng employee

        public int OrdersProcessed { get; set; } = 0;  // Bilang ng orders na hinawakan

        [Range(0, 5)]
        public decimal Rating { get; set; } = 0;  // Customer rating (1-5 stars)
        [StringLength(500)]

        public string? Notes { get; set; }  // Performance notes/comments

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}