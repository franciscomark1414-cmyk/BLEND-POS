using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class User
    {
        public int Id { get; set; }
        [Required]
        public string Username { get; set; } = null!;
        [Required]
        public string Password { get; set; } = null!;

        public string Role { get; set; } = null!;
        public DateTime? UpdatedAt { get; set; }

        // ========== NEW EMPLOYEE FIELDS ==========
        [Required]
        public string? FullName { get; set; }

        public string? Phone { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string Status { get; set; } = "Active";

        public DateTime DateJoined { get; set; } = DateTime.Now;

        // ========== PERFORMANCE & ATTENDANCE FIELDS ==========
        public virtual ICollection<EmployeePerformance>? Performances { get; set; }
        public virtual ICollection<EmployeeAttendance>? Attendances { get; set; }

        [Range(0, 5)]
        public decimal AverageRating { get; set; } = 0;  // Average rating ng employee

        public int TotalOrdersProcessed { get; set; } = 0;  // Lifetime orders
    }
}
