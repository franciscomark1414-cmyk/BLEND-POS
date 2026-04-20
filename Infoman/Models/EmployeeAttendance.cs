using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public enum AttendanceStatus
    {
        Present,      // Naka-check in at nagtrabaho
        Absent,       // Walang pasok ng walang leave
        Late,         // Late pumasok
        OnLeave,      // Naka-leave (approved)
        HalfDay,      // Kalahating araw lang
        Holiday       // Holiday (no work)
    }

    public class EmployeeAttendance
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? Employee { get; set; }

        public DateTime Date { get; set; } = DateTime.Today;

        public AttendanceStatus Status { get; set; } = AttendanceStatus.Present;

        public TimeSpan? CheckInTime { get; set; }  // e.g., 08:00 AM
        public TimeSpan? CheckOutTime { get; set; } // e.g., 05:00 PM

        [Range(0, 24)]
        public decimal HoursWorked { get; set; } = 0;  // Total hours worked

        public string? LeaveReason { get; set; }  // Reason kung naka-leave

        public bool IsApproved { get; set; } = true;  // For leave requests

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}