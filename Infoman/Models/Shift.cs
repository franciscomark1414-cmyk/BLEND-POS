using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infoman.Models
{
    public class Shift
    {
        public int ShiftId { get; set; }

        // Sino ang nag-open ng shift
        public int StaffUserId { get; set; }

        // Kailan nag-open at nag-close
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        // Pera
        public decimal OpeningFloat { get; set; }          // Ang binigay na panimulang pera
        public decimal? ExpectedCash { get; set; }         // Kinompute ng system
        public decimal? PhysicalCashCounted { get; set; }  // Binilang ng staff
        public decimal? Variance { get; set; }             // Physical - Expected

        // Status: "OPEN", "CLOSED", "FORCE_CLOSED"
        public string Status { get; set; } = "OPEN";

        // Notes kapag nag-close (optional pero maganda kung may notes)
        public string? ClosingNotes { get; set; }

        // Kung sino ang nag-close (null kung same staff na nag-open, may value kung ibang staff)
        public int? ClosedByUserId { get; set; }

        // Para sa audit trail
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties (para ma-link sa Users, Orders, at Expenses)
        public User? StaffUser { get; set; }
        public User? ClosedByUser { get; set; }
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
        [Column(TypeName = "decimal(18,2)")]
        public decimal? GcashCounted { get; set; }

        public ICollection<ShiftCashCount> CashCounts { get; set; } = new List<ShiftCashCount>();
    }
}