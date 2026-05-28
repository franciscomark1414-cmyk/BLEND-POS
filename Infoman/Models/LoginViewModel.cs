using System.ComponentModel.DataAnnotations;

namespace Infoman.Models
{
    public class LoginViewModel
    {
        [StringLength(100)]
        public string Username { get; set; } = null!;
        [StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = null!;
    }
}
