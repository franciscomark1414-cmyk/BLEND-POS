using System.ComponentModel.DataAnnotations;

public class CashWithdrawal
{
    [Key]
    public int WithdrawalId { get; set; }
    public DateTime WithdrawalDate { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; }
    public string RecordedBy { get; set; } = "Owner";
    public DateTime DateCreated { get; set; } = DateTime.Now;
}