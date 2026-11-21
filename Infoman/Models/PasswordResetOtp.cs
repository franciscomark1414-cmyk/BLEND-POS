namespace Infoman.Models
{
    public class PasswordResetOtp
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; } = null!;
        public string OtpHash { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int FailedAttempts { get; set; }
        public bool IsUsed { get; set; }
    }
}