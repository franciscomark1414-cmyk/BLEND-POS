// Services/RefundService.cs
using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class RefundService
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;

        public RefundService(ConnectionDatabase db, FIFOService fifoService)
        {
            _db = db;
            _fifoService = fifoService;
        }

        public RefundService(ConnectionDatabase db)
        {
            _db = db;
        }

        #region Private Helpers

        private async Task<decimal> CalculateRefundAmount(int orderId, List<int> itemIds)
        {
            var items = await _db.OrderItems
                .Include(oi => oi.AddOns) // ✅ ISAMA ANG ADD-ONS
                .Where(oi => oi.OrderId == orderId && itemIds.Contains(oi.OrderItemId) && !oi.IsRefunded)
                .ToListAsync();

            decimal totalRefund = 0;

            foreach (var item in items)
            {
                // Base product price
                totalRefund += item.Price * item.Quantity;

                // ✅ Add-ons price
                totalRefund += item.AddOns.Sum(a => a.Price * a.Quantity * item.Quantity);
            }

            return totalRefund;
        }

        private string GenerateRefundReference()
        {
            return $"RFD-{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";
        }

        private async Task UpdateMainStock(int rawMaterialId)
        {
            var totalStock = await _db.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var material = await _db.RawMaterials.FindAsync(rawMaterialId);
            if (material != null)
                material.Stock = totalStock;
        }

        /// <summary>
        /// Return ingredients to stock (for items NOT prepared/blended)
        /// </summary>
        private async Task ReturnIngredientsToStock(OrderItem orderItem, int quantityToReturn, string referenceNumber)
        {
            // ==========================================
            // PART 1: BASE PRODUCT RETURN (Existing)
            // ==========================================
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == orderItem.ProductSizeId);

            if (productSize != null && productSize.RecipeItems != null)
            {
                foreach (var recipe in productSize.RecipeItems)
                {
                    decimal quantityToAdd = recipe.Quantity * quantityToReturn;

                    if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                    {
                        quantityToAdd = quantityToAdd / 1000;
                    }

                    var batch = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipe.RawMaterialId)
                        .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                        .FirstOrDefaultAsync();

                    if (batch != null)
                    {
                        batch.QuantityRemaining += quantityToAdd;
                    }

                    await UpdateMainStock(recipe.RawMaterialId);
                }
            }

            // ==========================================
            // PART 2: ADD-ONS RETURN (Bago)
            // ==========================================
            if (orderItem.AddOns != null && orderItem.AddOns.Any())
            {
                foreach (var addOn in orderItem.AddOns)
                {
                    var addOnRecipe = await _db.AddOnRecipeItems
                        .Include(ar => ar.RawMaterial)
                        .Where(ar => ar.AddOnId == addOn.AddOnId && !ar.IsDeleted)
                        .ToListAsync();

                    foreach (var recipe in addOnRecipe)
                    {
                        // Total quantity to return = recipe * order qty * add-on qty
                        decimal quantityToAdd = recipe.Quantity * quantityToReturn * addOn.Quantity;

                        if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                        {
                            quantityToAdd = quantityToAdd / 1000;
                        }

                        var batch = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId)
                            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                            .FirstOrDefaultAsync();

                        if (batch != null)
                        {
                            batch.QuantityRemaining += quantityToAdd;
                        }

                        await UpdateMainStock(recipe.RawMaterialId);
                    }
                }
            }
        }

        /// <summary>
        /// Log waste for items that were already prepared/blended
        /// </summary>
        private async Task LogWasteForPreparedItem(OrderItem orderItem, string reason, int userId)
        {
            // ==========================================
            // PART 1: BASE PRODUCT WASTE LOG (Existing)
            // ==========================================
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == orderItem.ProductSizeId);

            if (productSize != null && productSize.RecipeItems != null)
            {
                foreach (var recipe in productSize.RecipeItems)
                {
                    var rawMaterial = recipe.RawMaterial;
                    if (rawMaterial == null) continue;

                    decimal wasteQuantityInBaseUnit = recipe.Quantity * orderItem.Quantity;
                    decimal wasteQuantityInDisplayUnit = wasteQuantityInBaseUnit;

                    if (rawMaterial.BaseUnit == "g" || rawMaterial.BaseUnit == "mL")
                    {
                        wasteQuantityInDisplayUnit = wasteQuantityInBaseUnit / 1000;
                    }

                    var batch = await _db.RawMaterialBatches
                        .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                        .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                        .FirstOrDefaultAsync();

                    decimal unitPrice = batch?.UnitPrice ?? rawMaterial.Price;
                    decimal totalCost = wasteQuantityInDisplayUnit * unitPrice;

                    var wasteLog = new WasteLog
                    {
                        RawMaterialId = recipe.RawMaterialId,
                        ProductId = orderItem.ProductId,
                        Quantity = wasteQuantityInBaseUnit,
                        Reason = WasteLog.WasteReason.CustomerCancel,
                        Date = DateTime.Today,
                        RecordedBy = userId.ToString(),
                        Notes = $"REFUND from {orderItem.Order?.OrderNumber}: {reason}",
                        TotalCost = totalCost,
                        BatchId = batch?.BatchId
                    };
                    _db.WasteLogs.Add(wasteLog);
                }
            }

            // ==========================================
            // PART 2: ADD-ONS WASTE LOG (Bago)
            // ==========================================
            if (orderItem.AddOns != null && orderItem.AddOns.Any())
            {
                foreach (var addOn in orderItem.AddOns)
                {
                    var addOnRecipe = await _db.AddOnRecipeItems
                        .Include(ar => ar.RawMaterial)
                        .Where(ar => ar.AddOnId == addOn.AddOnId && !ar.IsDeleted)
                        .ToListAsync();

                    foreach (var recipe in addOnRecipe)
                    {
                        var rawMaterial = recipe.RawMaterial;
                        if (rawMaterial == null) continue;

                        // Total quantity = recipe * order qty * add-on qty
                        decimal wasteQtyBase = recipe.Quantity * orderItem.Quantity * addOn.Quantity;
                        decimal wasteQtyDisplay = wasteQtyBase;

                        if (rawMaterial.BaseUnit == "g" || rawMaterial.BaseUnit == "mL")
                        {
                            wasteQtyDisplay = wasteQtyBase / 1000;
                        }

                        var batch = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                            .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                            .FirstOrDefaultAsync();

                        decimal unitPrice = batch?.UnitPrice ?? rawMaterial.Price;
                        decimal totalCost = wasteQtyDisplay * unitPrice;

                        var wasteLog = new WasteLog
                        {
                            RawMaterialId = recipe.RawMaterialId,
                            ProductId = orderItem.ProductId,
                            Quantity = wasteQtyBase,
                            Reason = WasteLog.WasteReason.CustomerCancel,
                            Date = DateTime.Today,
                            RecordedBy = userId.ToString(),
                            Notes = $"REFUND ADD-ON from {orderItem.Order?.OrderNumber}: {reason}",
                            TotalCost = totalCost,
                            BatchId = batch?.BatchId
                        };
                        _db.WasteLogs.Add(wasteLog);
                    }
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Staff: Request a refund (needs owner approval)
        /// </summary>
        public async Task<(bool success, string message, int requestId)> RequestRefundAsync(
            int orderId,
            List<int> itemIds,
            string reason,
            int userId)
        {
            var order = await _db.Orders.FindAsync(orderId);

            if (order == null)
                return (false, "Order not found", 0);

            if (order.Status != "COMPLETED")
                return (false, "Only completed orders can be refunded", 0);

            // Create pending request using your existing model
            var pendingRequest = new PendingRequest
            {
                OrderId = orderId,
                RequestType = "REFUND",
                Reason = reason,
                ItemIds = string.Join(",", itemIds),
                Status = "PENDING",
                RequestedBy = userId,
                RequestedAt = DateTime.Now
            };

            _db.PendingRequests.Add(pendingRequest);
            await _db.SaveChangesAsync();

            return (true, "Refund request has been sent to Owner for approval", pendingRequest.Id);
        }

        /// <summary>
        /// Owner: Process approved refund
        /// </summary>
        public async Task<(bool success, string message)> ProcessApprovedRefundAsync(
            int requestId,
            int approverId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var pendingRequest = await _db.PendingRequests
                    .Include(r => r.Order)
                    .FirstOrDefaultAsync(r => r.Id == requestId);

                if (pendingRequest == null)
                    return (false, "Request not found");

                if (pendingRequest.Status != "PENDING")
                    return (false, "Request already processed");

                // Parse item IDs from your ItemIds string
                List<int> itemIds = new List<int>();
                if (!string.IsNullOrEmpty(pendingRequest.ItemIds))
                {
                    itemIds = pendingRequest.ItemIds
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(int.Parse)
                        .ToList();
                }

                decimal refundAmount = await CalculateRefundAmount(pendingRequest.OrderId, itemIds);

                if (refundAmount == 0)
                    return (false, "No valid items to refund");

                bool isPrepared = pendingRequest.Order.Status == "COMPLETED";

                // 1. Create adjustment record
                var adjustment = new OrderAdjustment
                {
                    OriginalOrderId = pendingRequest.OrderId,
                    AdjustmentType = "REFUND",
                    Amount = refundAmount,
                    Reason = pendingRequest.Reason,
                    ReferenceNumber = GenerateRefundReference(),
                    Status = "APPROVED",
                    ApprovedBy = approverId,
                    ApprovedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    CreatedBy = pendingRequest.RequestedBy
                };
                _db.OrderAdjustments.Add(adjustment);
                await _db.SaveChangesAsync();

                // 2. Create refund transaction
                var refund = new RefundTransaction
                {
                    AdjustmentId = adjustment.AdjustmentId,
                    OriginalOrderId = pendingRequest.OrderId,
                    Amount = refundAmount,
                    Reason = pendingRequest.Reason,
                    ProcessedBy = approverId,
                    RefundReference = adjustment.ReferenceNumber,
                    CreatedAt = DateTime.Now
                };
                _db.RefundTransactions.Add(refund);

                // 3. Process each item
                foreach (var itemId in itemIds)
                {
                    var orderItem = await _db.OrderItems
        .Include(oi => oi.ProductSize)
            .ThenInclude(ps => ps.RecipeItems)
        .Include(oi => oi.AddOns) // ✅ IDAGDAG ITO PARA MAKITA ANG ADD-ONS
        .Include(oi => oi.Order)  // ✅ IDAGDAG ITO PARA SA ORDER NUMBER SA NOTES
        .FirstOrDefaultAsync(oi => oi.OrderItemId == itemId);

                    if (orderItem != null && !orderItem.IsRefunded)
                    {
                        // Mark as refunded
                        orderItem.IsRefunded = true;
                        orderItem.RefundReason = pendingRequest.Reason;
                        orderItem.DispositionProcessedAt = DateTime.Now;
                        orderItem.DispositionProcessedBy = approverId;

                        // Handle inventory based on preparation status
                        if (isPrepared)
                        {
                            await LogWasteForPreparedItem(orderItem, pendingRequest.Reason, approverId);
                        }
                        else
                        {
                            await ReturnIngredientsToStock(orderItem, orderItem.Quantity, pendingRequest.OrderId.ToString());
                        }
                    }
                }

                // 4. Update order status if fully refunded
                var remainingItems = await _db.OrderItems
                    .CountAsync(oi => oi.OrderId == pendingRequest.OrderId && !oi.IsRefunded && !oi.IsVoided);

                if (remainingItems == 0)
                {
                    pendingRequest.Order.Status = "REFUNDED";
                }

                // Update original total amount if not set
                if (pendingRequest.Order.OriginalTotalAmount == 0)
                {
                    pendingRequest.Order.OriginalTotalAmount = pendingRequest.Order.TotalAmount;
                }

                pendingRequest.Status = "APPROVED";
                pendingRequest.ApprovedBy = approverId;
                pendingRequest.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                string wasteMsg = isPrepared ? " (Items were already blended - marked as waste)" : " (Inventory returned to stock)";
                return (true, $"Refund of ₱{refundAmount:N2} processed successfully{wasteMsg}");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Owner: Reject refund request
        /// </summary>
        public async Task<(bool success, string message)> RejectRefundAsync(
            int requestId,
            int approverId,
            string rejectionReason)
        {
            var pendingRequest = await _db.PendingRequests
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (pendingRequest == null)
                return (false, "Request not found");

            if (pendingRequest.Status != "PENDING")
                return (false, "Request already processed");

            pendingRequest.Status = "REJECTED";
            pendingRequest.ApprovedBy = approverId;
            pendingRequest.ApprovedAt = DateTime.Now;
            pendingRequest.RejectionReason = rejectionReason;

            await _db.SaveChangesAsync();

            return (true, "Refund request has been rejected");
        }

        #endregion
    }
}