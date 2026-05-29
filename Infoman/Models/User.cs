using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class User
    {
        public int Id { get; set; }
        [Required]
        [StringLength(100)]
        public string Username { get; set; } = null!;
        [Required]
        [StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = null!;
        [StringLength(50)]
        public string Role { get; set; } = null!;
        public DateTime? UpdatedAt { get; set; }

        // ========== NEW EMPLOYEE FIELDS ==========
        [Required]
        [StringLength(100)]
        public string? FullName { get; set; }
        [StringLength(50)]

        public string? Phone { get; set; }

        [EmailAddress]
        [StringLength(100)]
        public string? Email { get; set; }
        [StringLength(50)]

        public string Status { get; set; } = "Active";

        public DateTime DateJoined { get; set; } = DateTime.Now;
        public bool IsLoggedIn { get; set; } = false;  // ✅ CURRENT LOGIN STATUS
        public DateTime? LastLoginTime { get; set; }   // ✅ Last login time
        public DateTime? LastLogoutTime { get; set; }

        // ========== PERFORMANCE & ATTENDANCE FIELDS ==========
        public virtual ICollection<EmployeePerformance>? Performances { get; set; }
        public virtual ICollection<EmployeeAttendance>? Attendances { get; set; }

        [Range(0, 5)]
        public decimal AverageRating { get; set; } = 0;  // Average rating ng employee

        public int TotalOrdersProcessed { get; set; } = 0;  // Lifetime orders
        public bool IsDeleted { get; set; } = false;
    }
}
