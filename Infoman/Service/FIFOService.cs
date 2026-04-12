using Infoman.Models;
using Infoman.Data;
using System;

using System.Linq;
namespace Infoman.Service
{
    public class FIFOService
    {
        private readonly ConnectionDatabase _context;

        public FIFOService(ConnectionDatabase context)
        {
            _context = context;
        }

        /// <summary>
        /// Magbawas ng stock gamit ang FIFO method
        /// </summary>
        /// <param name="rawMaterialId">ID ng raw material (e.g., Mango)</param>
        /// <param name="quantityToDeduct">Dami na gagamitin (kg)</param>
        /// <param name="productId">ID ng product na ginamitan (e.g., Mango Shake)</param>
        /// <param name="referenceNo">Reference number (order/production #)</param>
        /// <returns>True kung successful, False kung kulang ang stock</returns>
        public bool DeductStock(int rawMaterialId, decimal quantity, int productId, string referenceNo)
        {
            using var transaction = _context.Database.BeginTransaction();

            try
            {
                // Kunin ang mga batches na may remaining stock, FIFO (oldest first)
                var batches = _context.RawMaterialBatches
                    .Where(b => b.RawMaterialId == rawMaterialId && b.QuantityRemaining > 0)
                    .OrderBy(b => b.ReceivedDate)
                    .ToList();

                if (!batches.Any())
                    return false;

                decimal remainingToDeduct = quantity;
                var usedBatches = new List<(RawMaterialBatch batch, decimal usedQty)>();

                foreach (var batch in batches)
                {
                    if (remainingToDeduct <= 0) break;

                    decimal deductFromBatch = Math.Min(remainingToDeduct, batch.QuantityRemaining);

                    usedBatches.Add((batch, deductFromBatch));
                    batch.QuantityRemaining -= deductFromBatch;
                    remainingToDeduct -= deductFromBatch;
                }

                if (remainingToDeduct > 0)
                    return false;

                // Mag-record ng bawat deduction per batch
                foreach (var (batch, usedQty) in usedBatches)
                {
                    var usage = new ProductionUsage
                    {
                        BatchId = batch.BatchId,
                        QuantityUsed = usedQty,
                        DateUsed = DateTime.Now,
                        ReferenceNo = referenceNo,
                        ProductId = productId
                    };
                    _context.ProductionUsages.Add(usage);
                }

                // ✅ IMPORTANTE: I-UPDATE ANG MAIN STOCK NG RAW MATERIAL
                var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
                if (rawMaterial != null)
                {
                    // I-compute ang total remaining stock mula sa lahat ng batches
                    decimal totalRemaining = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == rawMaterialId)
                        .Sum(b => b.QuantityRemaining);

                    rawMaterial.Stock = totalRemaining;  // ← ITO ANG KULANG MO!
                }

                _context.SaveChanges();
                transaction.Commit();

                return true;
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }
        /// <summary>
        /// I-update ang Stock column sa RawMaterials table (for display)
        /// </summary>
        private void UpdateMainStockColumn(int rawMaterialId)
        {
            var totalRemaining = _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .Sum(b => (decimal?)b.QuantityRemaining) ?? 0;

            var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
            if (rawMaterial != null)
            {
                rawMaterial.Stock = totalRemaining;
            }
        }

        /// <summary>
        /// Magdagdag ng bagong delivery/batch ng raw material
        /// </summary>
        public void AddBatch(int rawMaterialId, decimal quantity, decimal unitPrice, DateTime? expiryDate = null)
        {
            var batch = new RawMaterialBatch
            {
                RawMaterialId = rawMaterialId,
                QuantityReceived = quantity,
                QuantityRemaining = quantity,
                ReceivedDate = DateTime.Now,
                ExpiryDate = expiryDate,
                UnitPrice = unitPrice
            };

            _context.RawMaterialBatches.Add(batch);

            // I-update ang main Stock column
            _context.RawMaterialBatches.Add(batch);
            _context.SaveChanges(); // save muna batch

            UpdateMainStockColumn(rawMaterialId);
            _context.SaveChanges(); // then update stock
        }

        /// <summary>
        /// Tingnan ang batch history ng isang raw material
        /// </summary>
        // In FIFOService.cs - Update the method signature
        // In FIFOService.cs - Replace the GetBatchHistory method with this:
        public List<BatchHistoryViewModel> GetBatchHistory(int rawMaterialId)
        {
            var batches = _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .OrderBy(b => b.ReceivedDate)
                .Select(b => new BatchHistoryViewModel
                {
                    BatchId = b.BatchId,
                    ReceivedDate = b.ReceivedDate,
                    QuantityReceived = b.QuantityReceived,
                    QuantityRemaining = b.QuantityRemaining,
                    ExpiryDate = b.ExpiryDate,
                    UnitPrice = b.UnitPrice,
                    Usage = b.ProductionUsages.Select(u => new UsageViewModel
                    {
                        QuantityUsed = u.QuantityUsed,
                        DateUsed = u.DateUsed,
                        ReferenceNo = u.ReferenceNo
                    }).ToList()
                })
                .ToList();

            return batches;
        }

        // Add these ViewModel classes at the end of your FIFOService.cs file (outside the class)
        public class BatchHistoryViewModel
        {
            public int BatchId { get; set; }
            public DateTime ReceivedDate { get; set; }
            public decimal QuantityReceived { get; set; }
            public decimal QuantityRemaining { get; set; }
            public DateTime? ExpiryDate { get; set; }
            public decimal UnitPrice { get; set; }
            public List<UsageViewModel> Usage { get; set; }
        }

        public class UsageViewModel
        {
            public decimal QuantityUsed { get; set; }
            public DateTime DateUsed { get; set; }
            public string ReferenceNo { get; set; }
        }
    }
}
