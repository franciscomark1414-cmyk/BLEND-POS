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
        public bool DeductStock(int rawMaterialId, decimal quantityToDeduct, int productId, string referenceNo = "")
        {
            if (quantityToDeduct <= 0)
                return true;

            // Kunin ang lahat ng batches na may natitirang stock
            // Inorder by ReceivedDate (FIFO: pinakamatanda muna)
            var availableBatches = _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId && b.QuantityRemaining > 0)
                .OrderBy(b => b.ReceivedDate)  // ← FIFO logic dito
                .ToList();

            // Compute total available stock
            decimal totalAvailable = availableBatches.Sum(b => b.QuantityRemaining);

            if (totalAvailable < quantityToDeduct)
                return false;  // Insufficient stock

            decimal remainingToDeduct = quantityToDeduct;

            // Loop sa bawat batch at bawasan simula sa pinakamatanda
            foreach (var batch in availableBatches)
            {
                if (remainingToDeduct <= 0)
                    break;

                decimal deductFromThisBatch = Math.Min(batch.QuantityRemaining, remainingToDeduct);

                // Bawasan ang batch
                batch.QuantityRemaining -= deductFromThisBatch;
                remainingToDeduct -= deductFromThisBatch;

                // I-record kung saang batch galing ang ginamit
                var usage = new ProductionUsage
                {
                    BatchId = batch.BatchId,
                    ProductId = productId,
                    QuantityUsed = deductFromThisBatch,
                    DateUsed = DateTime.Now,
                    ReferenceNo = referenceNo
                };

                _context.ProductionUsages.Add(usage);
            }

            // I-update ang main Stock column (for display purposes only)
            UpdateMainStockColumn(rawMaterialId);

            _context.SaveChanges();
            return true;
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
            UpdateMainStockColumn(rawMaterialId);

            _context.SaveChanges();
        }

        /// <summary>
        /// Tingnan ang batch history ng isang raw material
        /// </summary>
        public dynamic GetBatchHistory(int rawMaterialId)
        {
            return _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .OrderByDescending(b => b.ReceivedDate)
                .Select(b => new
                {
                    b.BatchId,
                    b.ReceivedDate,
                    b.QuantityReceived,
                    b.QuantityRemaining,
                    b.ExpiryDate,
                    b.UnitPrice,
                    Usage = b.ProductionUsages.Select(u => new
                    {
                        u.QuantityUsed,
                        u.DateUsed,
                        u.ReferenceNo
                    })
                })
                .ToList();
        }
    }
}
