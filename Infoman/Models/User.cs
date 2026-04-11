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
    }
}
