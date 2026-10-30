using System;

namespace Infoman.ViewModels
{
    public class WithdrawalViewModel
    {
        public DateTime WithdrawalDate { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; }
        public string RecordedBy { get; set; }
    }
}