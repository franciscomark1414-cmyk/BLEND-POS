using System;
using System.Collections.Generic;

namespace Infoman.ViewModels
{
    public class FinancialSummaryViewModel
    {
        public decimal TotalSales { get; set; }
        public decimal RawMaterialPurchases { get; set; }
        public decimal OtherExpenses { get; set; }
        public decimal CashInflow { get; set; }
        public decimal CashOutflow { get; set; }
        public decimal CashMovement { get; set; }
        public decimal COGS { get; set; }
        public decimal Profit { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class ExpenseViewModel
    {
        public int ExpenseId { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string Category { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; }
        public string RecordedBy { get; set; }
        public DateTime DateCreated { get; set; }
    }
}