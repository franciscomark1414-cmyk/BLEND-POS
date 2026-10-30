using System;
using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class Expense
    {
        [Key]
        public int ExpenseId { get; set; }

        [Required]
        public DateTime ExpenseDate { get; set; }

        [Required]
        [StringLength(50)]
        public string Category { get; set; } // Electricity, Water, Internet, Rent, Repairs, Payroll, Others

        [Required]
        public decimal Amount { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        [StringLength(100)]
        public string RecordedBy { get; set; } = "Owner";

        public DateTime DateCreated { get; set; } = DateTime.Now;
    }
}