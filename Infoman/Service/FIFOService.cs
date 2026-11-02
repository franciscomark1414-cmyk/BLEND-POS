using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

                // ✅ I-CONVERT SA DISPLAY UNIT BAGO MAG-COMPUTE NG COST
                decimal qtyInDisplayUnit = UnitConverter.ToCostUnitQty(batch.QuantityRemaining, batch.RawMaterial.BaseUnit);

                var wasteLog = new WasteLog
                {
                    RawMaterialId = batch.RawMaterialId,
                    Quantity = batch.QuantityRemaining, // Base unit pa rin ang i-save sa DB
                    Reason = WasteReason.Expired,
                    Date = DateTime.Today,
                    RecordedBy = "System",
                    Notes = $"Auto-generated from expired batch received on {batch.ReceivedDate}",
                    BatchId = batch.BatchId,
                    TotalCost = qtyInDisplayUnit * batch.UnitPrice // ✅ TAMA NA
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
        public bool DeductStock(int rawMaterialId, decimal quantityNeededForRecipe, string unitUsed, int productId, string referenceNo, int? orderId = null, int? productSizeId = null)
        {
            var rawMaterial = _context.RawMaterials.Find(rawMaterialId);
            if (rawMaterial == null) return false;

            decimal quantityInBaseUnit = ConvertToBaseUnit(quantityNeededForRecipe, unitUsed, rawMaterial.BaseUnit);

            var (success, _, _) = DeductStockWithCostAsync(
                rawMaterialId,
                quantityInBaseUnit,
                productId,
                referenceNo,
                orderId,
                productSizeId).GetAwaiter().GetResult();

            return success;
        }

        // ========== HELPER: UNIT CONVERSION ==========
        private decimal ConvertToBaseUnit(decimal quantity, string fromUnit, string toBaseUnit)
        {
            return UnitConverter.ToBaseUnit(quantity, fromUnit, toBaseUnit);
        }

        // ========== 3. DEDUCT STOCK (ASYNC - BAGONG VERSION) ==========
        public async Task<bool> DeductStockAsync(int rawMaterialId, decimal quantityInBaseUnit, int productId, string referenceNo, int? orderId = null, int? productSizeId = null)
        {
            var (success, _, _) = await DeductStockWithCostAsync(
                rawMaterialId,
                quantityInBaseUnit,
                productId,
                referenceNo,
                orderId,
                productSizeId);

            return success;
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
            // ✅ CHECK KUNG MAY EXISTING TRANSACTION NA (e.g., galing sa ExchangeService)
            var currentTransaction = _context.Database.CurrentTransaction;
            bool shouldCreateTransaction = currentTransaction == null;
            IDbContextTransaction transaction = null;

            if (shouldCreateTransaction)
            {
                transaction = await _context.Database.BeginTransactionAsync();
            }

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
                {
                    if (shouldCreateTransaction && transaction != null)
                        await transaction.RollbackAsync();
                    return (false, 0, null);
                }

                decimal remainingToDeduct = quantity;
                var usedBatches = new List<BatchUsageDetail>();
                decimal totalCost = 0;

                foreach (var batch in batches)
                {
                    if (remainingToDeduct <= 0) break;

                    decimal deductFromBatch = Math.Min(remainingToDeduct, batch.QuantityRemaining);

                    // ✅ I-CONVERT SA DISPLAY UNIT PARA SA COSTING
                    decimal deductInDisplayUnit = UnitConverter.ToCostUnitQty(deductFromBatch, batch.RawMaterial?.BaseUnit);

                    decimal batchCost = deductInDisplayUnit * batch.UnitPrice;

                    usedBatches.Add(new BatchUsageDetail
                    {
                        BatchId = batch.BatchId,
                        QuantityUsed = deductFromBatch,
                        UnitPrice = batch.UnitPrice,
                        TotalCost = batchCost,
                        BaseUnit = batch.RawMaterial?.BaseUnit
                    });

                    batch.QuantityRemaining -= deductFromBatch;
                    remainingToDeduct -= deductFromBatch;
                    totalCost += batchCost;
                }

                if (remainingToDeduct > 0.001m)
                {
                    if (shouldCreateTransaction && transaction != null)
                        await transaction.RollbackAsync();
                    return (false, 0, null);
                }

                foreach (var usage in usedBatches)
                {
                    // Cost per cost unit galing sa batch (₱ per kg / L / pcs)
                    // Ang usage.TotalCost ay na-compute na sa loop kanina gamit ang UnitConverter

                    _context.ProductionUsages.Add(new ProductionUsage
                    {
                        BatchId = usage.BatchId,
                        QuantityUsed = usage.QuantityUsed,
                        DateUsed = DateTime.Now,
                        ReferenceNo = referenceNo,
                        ProductId = productId,
                        OrderId = orderId,
                        ProductSizeId = productSizeId,
                        CostPerCostUnit = usage.UnitPrice,
                        TotalCost = Math.Round(usage.TotalCost, 2)
                    });
                }

                await UpdateMainStockColumn(rawMaterialId);
                await _context.SaveChangesAsync();

                // ✅ I-COMMIT LANG KUNG TAYO ANG NAG-OPEN NG TRANSACTION
                if (shouldCreateTransaction && transaction != null)
                {
                    await transaction.CommitAsync();
                }

                return (true, totalCost, usedBatches);
            }
            catch (Exception ex)
            {
                // ✅ I-ROLLBACK LANG KUNG TAYO ANG NAG-OPEN NG TRANSACTION
                // Kung galing sa ExchangeService, hahayaan natin sila ang mag-rollback
                if (shouldCreateTransaction && transaction != null)
                {
                    await transaction.RollbackAsync();
                }
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
            // ✅ KUNIN ANG STOCK MULA SA IN-MEMORY BATCHES, HINDI SA DATABASE
            var batches = await _context.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .ToListAsync();

            decimal totalRemaining = batches.Sum(b => b.QuantityRemaining);

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

                    // ✅ I-CONVERT SA DISPLAY UNIT PARA SA COSTING
                    decimal wastedQtyInDisplayUnit = UnitConverter.ToCostUnitQty(wastedQty, batch.RawMaterial?.BaseUnit);

                    var wasteLog = new WasteLog
                    {
                        RawMaterialId = batch.RawMaterialId,
                        Quantity = wastedQty, // Base unit sa DB
                        Reason = WasteReason.Damaged,
                        Date = DateTime.Today,
                        RecordedBy = "Owner",
                        Notes = $"Manual adjustment from batch #{batchId}: {reason}",
                        TotalCost = wastedQtyInDisplayUnit * batch.UnitPrice // ✅ IDINAGDAG ANG COST
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
        public string? BaseUnit { get; set; }
    }
}