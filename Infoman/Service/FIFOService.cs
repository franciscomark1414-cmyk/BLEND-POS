using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using static Infoman.Models.WasteLog;

namespace Infoman.Service
{
    public class FIFOService
    {
        private readonly ConnectionDatabase _context;

        public FIFOService(ConnectionDatabase context)
        {
            _context = context;
        }

        // ========== 1. PROCESS EXPIRED BATCHES ==========
        public async Task ProcessExpiredBatches()
        {
            var today = DateTime.Today;

            var expiredBatches = await _context.RawMaterialBatches
                .Include(b => b.RawMaterial)
                .Where(b => b.ExpiryDate.HasValue
                    && b.ExpiryDate.Value < today
                    && b.QuantityRemaining > 0)
                .ToListAsync();

            foreach (var batch in expiredBatches)
            {
                // I-move ang stock sa waste
                var wasteLog = new WasteLog
                {
                    RawMaterialId = batch.RawMaterialId,
                    Quantity = batch.QuantityRemaining,
                    Reason = WasteReason.Expired,
                    Date = DateTime.Today,
                    RecordedBy = "System",
                    Notes = $"Auto-generated from expired batch received on {batch.ReceivedDate}",
                    BatchId = batch.BatchId,  // ✅ SAVE THE BATCH ID
                    TotalCost = batch.QuantityRemaining * batch.UnitPrice
                };

                _context.WasteLogs.Add(wasteLog);
                batch.QuantityRemaining = 0;
            }

            if (expiredBatches.Any())
            {
                await _context.SaveChangesAsync();

                // 🔥 IDAGDAG ITO: I-update ang main stock ng LAHAT ng apektadong raw materials
                var affectedMaterialIds = expiredBatches.Select(b => b.RawMaterialId).Distinct();
                foreach (var materialId in affectedMaterialIds)
                {
                    await UpdateMainStockColumn(materialId);
                }
                await _context.SaveChangesAsync();
            }
        }



        // ========== 2. DEDUCT STOCK (SYNCHRONOUS - PARA SA EXISTING CODE) ==========
        // ========== BAGONG METHOD (may unitUsed) ==========
        // ========== 2. DEDUCT STOCK (SYNCHRONOUS - MAY UNIT CONVERSION) ==========
        // ========== 2. DEDUCT STOCK (SYNCHRONOUS) ==========
        public bool DeductStock(int rawMaterialId, decimal quantityNeededForRecipe, string unitUsed, int productId, string referenceNo, int? orderId = null, int? productSizeId = null)
        {
            var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
            if (rawMaterial == null) return false;

            // Convert recipe quantity to base unit
            decimal quantityInBaseUnit = ConvertToBaseUnit(quantityNeededForRecipe, unitUsed, rawMaterial.BaseUnit);

            System.Diagnostics.Debug.WriteLine($"=== DEDUCT STOCK ===");
            System.Diagnostics.Debug.WriteLine($"Material: {rawMaterial.Name} | BaseUnit: {rawMaterial.BaseUnit}");
            System.Diagnostics.Debug.WriteLine($"Recipe: {quantityNeededForRecipe} {unitUsed} → {quantityInBaseUnit} {rawMaterial.BaseUnit}");

            return DeductStockAsync(rawMaterialId, quantityInBaseUnit, productId, referenceNo, orderId, productSizeId).GetAwaiter().GetResult();
        }

        // ========== HELPER: UNIT CONVERSION ==========
        private decimal ConvertToBaseUnit(decimal quantity, string fromUnit, string toBaseUnit)
        {
            if (fromUnit == toBaseUnit) return quantity;

            return (fromUnit, toBaseUnit) switch
            {
                ("kg", "g") => quantity * 1000,
                ("g", "kg") => quantity / 1000,
                ("L", "mL") => quantity * 1000,
                ("mL", "L") => quantity / 1000,
                _ => quantity  // pcs or same unit — no conversion
            };
        }

        // ========== 3. DEDUCT STOCK (ASYNC - BAGONG VERSION) ==========
        // ========== 3. DEDUCT STOCK (ASYNC) ==========
        // IMPORTANT: quantity must already be in the material's BaseUnit (g, mL, kg, L, pcs)
        // No conversion is done here — caller is responsible.
        public async Task<bool> DeductStockAsync(int rawMaterialId, decimal quantityInBaseUnit, int productId, string referenceNo, int? orderId = null, int? productSizeId = null)
        {
            var rawMaterial = await _context.RawMaterials.FindAsync(rawMaterialId);
            if (rawMaterial == null) return false;

            System.Diagnostics.Debug.WriteLine($"=== DEDUCTSTOCKASYNC ===");
            System.Diagnostics.Debug.WriteLine($"Material: {rawMaterial.Name} | Deducting: {quantityInBaseUnit} {rawMaterial.BaseUnit}");

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await ProcessExpiredBatches();

                var batches = await _context.RawMaterialBatches
                    .Include(b => b.RawMaterial)
                    .Where(b => b.RawMaterialId == rawMaterialId
                        && b.QuantityRemaining > 0
                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                    .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)
                    .ThenBy(b => b.ReceivedDate)
                    .ToListAsync();

                System.Diagnostics.Debug.WriteLine($"Batches available: {batches.Count}");

                if (!batches.Any()) return false;

                decimal remainingToDeduct = quantityInBaseUnit;
                var usedBatches = new List<(RawMaterialBatch batch, decimal usedQty)>();

                foreach (var batch in batches)
                {
                    if (remainingToDeduct <= 0) break;

                    decimal deductFromBatch = Math.Min(remainingToDeduct, batch.QuantityRemaining);
                    usedBatches.Add((batch, deductFromBatch));
                    batch.QuantityRemaining -= deductFromBatch;
                    remainingToDeduct -= deductFromBatch;

                    System.Diagnostics.Debug.WriteLine($"Batch {batch.BatchId}: deducted {deductFromBatch}, remaining in batch: {batch.QuantityRemaining}");
                }

                // Small tolerance for floating point errors (e.g. 0.0000001)
                if (remainingToDeduct > 0.001m)
                {
                    System.Diagnostics.Debug.WriteLine($"FAILED: Still need {remainingToDeduct} but no more batches");
                    return false;
                }

                foreach (var (batch, usedQty) in usedBatches)
                {
                    _context.ProductionUsages.Add(new ProductionUsage
                    {
                        BatchId = batch.BatchId,
                        QuantityUsed = usedQty,
                        DateUsed = DateTime.Now,
                        ReferenceNo = referenceNo,
                        ProductId = productId,
                        OrderId = orderId,           
                        ProductSizeId = productSizeId
                    });
                }

                await UpdateMainStockColumn(rawMaterialId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                System.Diagnostics.Debug.WriteLine($"SUCCESS: Deducted {quantityInBaseUnit} {rawMaterial.BaseUnit}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                await transaction.RollbackAsync();
                throw;
            }
        }


        // ========== 4. DEDUCT STOCK WITH COST TRACKING ==========
        public async Task<(bool success, decimal totalCost, List<BatchUsageDetail> usedBatches)> DeductStockWithCostAsync(
     int rawMaterialId,
     decimal quantity,
     int productId,
     string referenceNo,
     int? orderId = null,
     int? productSizeId = null)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await ProcessExpiredBatches();

                var batches = await _context.RawMaterialBatches
                    .Where(b => b.RawMaterialId == rawMaterialId
                        && b.QuantityRemaining > 0
                        && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                    .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue)
                    .ThenBy(b => b.ReceivedDate)
                    .ToListAsync();

                if (!batches.Any())
                    return (false, 0, null);

                decimal remainingToDeduct = quantity;
                var usedBatches = new List<BatchUsageDetail>();
                decimal totalCost = 0;

                foreach (var batch in batches)
                {
                    if (remainingToDeduct <= 0) break;

                    decimal deductFromBatch = Math.Min(remainingToDeduct, batch.QuantityRemaining);
                    decimal batchCost = deductFromBatch * batch.UnitPrice;

                    usedBatches.Add(new BatchUsageDetail
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

                if (remainingToDeduct > 0.001m)
                    return (false, 0, null);

                foreach (var usage in usedBatches)
                {
                    _context.ProductionUsages.Add(new ProductionUsage
                    {
                        BatchId = usage.BatchId,
                        QuantityUsed = usage.QuantityUsed,
                        DateUsed = DateTime.Now,
                        ReferenceNo = referenceNo,
                        ProductId = productId,
                        OrderId = orderId,           // ✅ IDAGDAG ITO
                        ProductSizeId = productSizeId  // ✅ IDAGDAG ITO
                    });
                }

                await UpdateMainStockColumn(rawMaterialId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, totalCost, usedBatches);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ========== 5. GET ACTIVE BATCH PRICE (FIFO/FEFO) ==========
        public async Task<decimal> GetActiveBatchPrice(int rawMaterialId)
        {
            // I-process muna ang expired batches
            await ProcessExpiredBatches();

            // Kunin ang pinakamatandang NON-EXPIRED batch na may stock
            var activeBatch = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId
                    && b.QuantityRemaining > 0
                    && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                .OrderBy(b => b.ExpiryDate.HasValue ? b.ExpiryDate.Value : DateTime.MaxValue) // FEFO
                .ThenBy(b => b.ReceivedDate)
                .FirstOrDefaultAsync();

            if (activeBatch != null)
                return activeBatch.UnitPrice;

            // Kung walang active batch, kunin ang default price
            var material = await _context.RawMaterials.FindAsync(rawMaterialId);
            return material?.Price ?? 0;
        }

        // ========== 6. GET BATCH HISTORY ==========
        public async Task<List<BatchHistoryViewModel>> GetBatchHistoryAsync(int rawMaterialId)
        {
            // I-process muna ang expired batches para updated ang status
            await ProcessExpiredBatches();

            var batches = await _context.RawMaterialBatches
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
                    IsExpired = b.ExpiryDate.HasValue && b.ExpiryDate.Value < DateTime.Today && b.QuantityRemaining > 0,
                    Usage = b.ProductionUsages.Select(u => new UsageViewModel
                    {
                        QuantityUsed = u.QuantityUsed,
                        DateUsed = u.DateUsed,
                        ReferenceNo = u.ReferenceNo
                    }).ToList()
                })
                .ToListAsync();

            return batches;
        }

        // ========== 7. SYNC VERSION NG GET BATCH HISTORY (para sa existing code) ==========
        public List<BatchHistoryViewModel> GetBatchHistory(int rawMaterialId)
        {
            return GetBatchHistoryAsync(rawMaterialId).GetAwaiter().GetResult();
        }

        // ========== 8. ADD BATCH ==========
        public async Task AddBatchAsync(int rawMaterialId, decimal quantity, decimal unitPrice, DateTime? expiryDate = null)
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
            await _context.SaveChangesAsync();

            await UpdateMainStockColumn(rawMaterialId);
            await _context.SaveChangesAsync();
        }

        // ========== 9. HELPER: UPDATE MAIN STOCK COLUMN ==========
        private async Task UpdateMainStockColumn(int rawMaterialId)
        {
            var totalRemaining = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var rawMaterial = await _context.RawMaterials.FindAsync(rawMaterialId);
            if (rawMaterial != null)
            {
                rawMaterial.Stock = totalRemaining;
            }
        }

        // ========== 10. MANUAL ADJUSTMENT NG BATCH STOCK ==========
        public async Task<bool> AdjustBatchStockAsync(int batchId, decimal newQuantity, string reason)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var batch = await _context.RawMaterialBatches
                    .Include(b => b.RawMaterial)
                    .FirstOrDefaultAsync(b => b.BatchId == batchId);

                if (batch == null)
                    return false;

                decimal oldQuantity = batch.QuantityRemaining;

                if (newQuantity < oldQuantity)
                {
                    // May nabawas (spoilage/damage) - gumawa ng waste log
                    decimal wastedQty = oldQuantity - newQuantity;

                    var wasteLog = new WasteLog
                    {
                        RawMaterialId = batch.RawMaterialId,
                        Quantity = wastedQty,
                        Reason = WasteReason.Damaged,
                        Date = DateTime.Today,
                        RecordedBy = "Owner",
                        Notes = $"Manual adjustment from batch #{batchId}: {reason}"
                    };
                    _context.WasteLogs.Add(wasteLog);
                }

                batch.QuantityRemaining = newQuantity;
                await UpdateMainStockColumn(batch.RawMaterialId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // ========== 11. MARK BATCH AS EXPIRED ==========
        public async Task<bool> MarkBatchAsExpiredAsync(int batchId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var batch = await _context.RawMaterialBatches
                    .Include(b => b.RawMaterial)
                    .FirstOrDefaultAsync(b => b.BatchId == batchId);

                if (batch == null || batch.QuantityRemaining <= 0)
                    return false;

                var wasteLog = new WasteLog
                {
                    RawMaterialId = batch.RawMaterialId,
                    Quantity = batch.QuantityRemaining,
                    Reason = WasteReason.Expired,
                    Date = DateTime.Today,
                    RecordedBy = "System",
                    Notes = $"Manually marked as expired from batch #{batch.BatchId}"
                };

                _context.WasteLogs.Add(wasteLog);
                batch.QuantityRemaining = 0;

                await UpdateMainStockColumn(batch.RawMaterialId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    // ========== VIEWMODELS ==========
    public class BatchHistoryViewModel
    {
        public int BatchId { get; set; }
        public DateTime ReceivedDate { get; set; }
        public decimal QuantityReceived { get; set; }
        public decimal QuantityRemaining { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsExpired { get; set; }
        public List<UsageViewModel> Usage { get; set; }
    }

    public class UsageViewModel
    {
        public decimal QuantityUsed { get; set; }
        public DateTime DateUsed { get; set; }
        public string ReferenceNo { get; set; }
    }

    public class BatchUsageDetail
    {
        public int BatchId { get; set; }
        public decimal QuantityUsed { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalCost { get; set; }
    }
}