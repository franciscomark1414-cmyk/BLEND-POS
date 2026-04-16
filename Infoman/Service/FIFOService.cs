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
                // ✅ FEFO with secondary sort by Received Date
                var batches = _context.RawMaterialBatches
                    .Where(b => b.RawMaterialId == rawMaterialId && b.QuantityRemaining > 0)
                    .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)  // Primary: Expiry (null = max)
                    .ThenBy(b => b.ReceivedDate)  // Secondary: Received date
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

                // I-update ang main stock
                var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
                if (rawMaterial != null)
                {
                    decimal totalRemaining = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == rawMaterialId)
                        .Sum(b => b.QuantityRemaining);

                    rawMaterial.Stock = totalRemaining;
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

        // FIFOService.cs - Baguhin ang return type at mag-track ng cost

        /// <summary>
        /// Magbawas ng stock gamit ang FEFO (First Expiry, First Out)
        /// Returns: (success, totalCost, usedBatchesDetails)
        /// </summary>
        public (bool success, decimal totalCost, List<BatchUsage> usedBatches) DeductStockWithCost(
            int rawMaterialId,
            decimal quantity,
            int productId,
            string referenceNo)
        {
            using var transaction = _context.Database.BeginTransaction();

            try
            {
                // FEFO: Order by Expiry Date (nulls last), then by Received Date
                var batches = _context.RawMaterialBatches
                    .Where(b => b.RawMaterialId == rawMaterialId && b.QuantityRemaining > 0)
                    .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)
                    .ThenBy(b => b.ReceivedDate)
                    .ToList();

                if (!batches.Any())
                    return (false, 0, null);

                decimal remainingToDeduct = quantity;
                var usedBatches = new List<BatchUsage>();
                decimal totalCost = 0;

                foreach (var batch in batches)
                {
                    if (remainingToDeduct <= 0) break;

                    decimal deductFromBatch = Math.Min(remainingToDeduct, batch.QuantityRemaining);
                    decimal batchCost = deductFromBatch * batch.UnitPrice;  // ← ITO ANG ACTUAL COST

                    usedBatches.Add(new BatchUsage
                    {
                        BatchId = batch.BatchId,
                        QuantityUsed = deductFromBatch,
                        UnitPrice = batch.UnitPrice,
                        TotalCost = batchCost
                    });

                    batch.QuantityRemaining -= deductFromBatch;
                    remainingToDeduct -= deductFromBatch;
                    totalCost += batchCost;
                }

                if (remainingToDeduct > 0)
                    return (false, 0, null);

                // Mag-record ng bawat deduction per batch
                foreach (var usage in usedBatches)
                {
                    var productionUsage = new ProductionUsage
                    {
                        BatchId = usage.BatchId,
                        QuantityUsed = usage.QuantityUsed,
                        DateUsed = DateTime.Now,
                        ReferenceNo = referenceNo,
                        ProductId = productId
                    };
                    _context.ProductionUsages.Add(productionUsage);
                }

                // I-update ang main stock ng raw material
                var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
                if (rawMaterial != null)
                {
                    decimal totalRemaining = _context.RawMaterialBatches
                        .Where(b => b.RawMaterialId == rawMaterialId)
                        .Sum(b => b.QuantityRemaining);

                    rawMaterial.Stock = totalRemaining;
                }

                _context.SaveChanges();
                transaction.Commit();

                return (true, totalCost, usedBatches);
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        // Helper class para sa batch usage details
        public class BatchUsage
        {
            public int BatchId { get; set; }
            public decimal QuantityUsed { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal TotalCost { get; set; }
        }


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
                .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)  // Primary: Expiry
                .ThenBy(b => b.ReceivedDate)  // Secondary: Received date
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
