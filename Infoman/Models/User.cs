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

        // ========== NEW EMPLOYEE FIELDS ==========
        [Required]
        public string? FullName { get; set; }

        public string? Phone { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string Status { get; set; } = "Active";

        public DateTime DateJoined { get; set; } = DateTime.Now;
    }
}
