// Services/ExchangeService.cs - FIXED VERSION
using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class ExchangeService
    {
        private readonly ConnectionDatabase _db;
        private readonly FIFOService _fifoService;

        public ExchangeService(ConnectionDatabase db, FIFOService fifoService)
        {
            _db = db;
            _fifoService = fifoService;
        }

        /// <summary>
        /// Process an exchange transaction (NO StoreCredit, NO InventoryConsumptionLog)
        /// </summary>
        public async Task<(bool success, string message, int exchangeId, bool requiresApproval)> ProcessExchangeAsync(
            int originalOrderId,
            int userId,  // ✅ CHANGED: from cashierId to userId
            List<ExchangeItemRequest> itemsToReturn,
            List<ExchangeItemRequest> itemsToReplace,
            string reason)
        {
            var originalOrder = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == originalOrderId);

            if (originalOrder == null)
                return (false, "Original order not found", 0, false);

            if (originalOrder.Status != "COMPLETED")
                return (false, "Only completed orders can be exchanged", 0, false);

            // Ensure OriginalTotalAmount is set
            if (originalOrder.OriginalTotalAmount == 0)
            {
                originalOrder.OriginalTotalAmount = originalOrder.TotalAmount;
            }

            decimal totalReturnedValue = 0;
            decimal totalReplacementValue = 0;
            var processedReturnItems = new List<ExchangeItem>();

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // STEP 1: Process RETURNED items
                foreach (var returnItem in itemsToReturn)
                {
                    var orderItem = await _db.OrderItems
                        .Include(oi => oi.ProductSize)
                            .ThenInclude(ps => ps.RecipeItems)
                                .ThenInclude(ri => ri.RawMaterial)
                        .FirstOrDefaultAsync(oi => oi.OrderItemId == returnItem.OrderItemId);

                    if (orderItem == null) continue;

                    // Check if quantity is valid
                    if (returnItem.Quantity > orderItem.Quantity)
                        return (false, $"Cannot return more than ordered quantity", 0, false);

                    // Check if item is already refunded or voided
                    if (orderItem.IsRefunded || orderItem.IsVoided)
                        return (false, $"Item already refunded or voided", 0, false);

                    decimal itemValue = orderItem.Price * returnItem.Quantity;
                    totalReturnedValue += itemValue;

                    // Return ingredients to stock
                    await ReturnIngredientsToStock(orderItem, returnItem.Quantity, originalOrder.OrderNumber);

                    // Mark item as exchanged (use IsRefunded flag for now)
                    orderItem.IsRefunded = true;
                    orderItem.RefundReason = $"EXCHANGE: {reason}";
                    orderItem.DispositionProcessedAt = DateTime.Now;
                    orderItem.DispositionProcessedBy = userId;  // ✅ FIXED: userId instead of cashierId

                    processedReturnItems.Add(new ExchangeItem
                    {
                        OriginalOrderItemId = returnItem.OrderItemId,
                        QuantityReturned = returnItem.Quantity,
                        OriginalPrice = orderItem.Price,
                        ReplacementProductSizeId = 0,
                        QuantityReplaced = 0,
                        ReplacementPrice = 0
                    });
                }

                // STEP 2: Process REPLACEMENT items
                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .Include(ps => ps.Product)
                        .Include(ps => ps.RecipeItems)
                            .ThenInclude(ri => ri.RawMaterial)
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize == null) continue;

                    // Check stock availability
                    foreach (var recipe in productSize.RecipeItems)
                    {
                        decimal neededQty = recipe.Quantity * replaceItem.Quantity;
                        decimal neededQtyInKg = neededQty;
                        if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                            neededQtyInKg = neededQty / 1000;

                        var availableQty = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                            .SumAsync(b => b.QuantityRemaining);

                        if (availableQty < neededQtyInKg)
                            return (false, $"Insufficient stock for {productSize.Product.Name} - {productSize.SizeName}", 0, false);
                    }

                    // Deduct raw materials
                    foreach (var recipe in productSize.RecipeItems)
                    {
                        decimal neededQty = recipe.Quantity * replaceItem.Quantity;
                        decimal neededQtyInKg = neededQty;
                        if (recipe.RawMaterial.BaseUnit == "g" || recipe.RawMaterial.BaseUnit == "mL")
                            neededQtyInKg = neededQty / 1000;

                        bool success = _fifoService.DeductStock(
                            recipe.RawMaterialId,
                            neededQtyInKg,
                            productSize.ProductId,
                            $"EXCHANGE-{originalOrder.OrderNumber}"
                        );

                        if (!success)
                            return (false, $"Failed to deduct stock for {productSize.Product.Name}", 0, false);
                    }

                    decimal itemValue = productSize.Price * replaceItem.Quantity;
                    totalReplacementValue += itemValue;
                }

                // STEP 3: Calculate price difference
                decimal priceDifference = totalReplacementValue - totalReturnedValue;
                decimal additionalPayment = priceDifference > 0 ? priceDifference : 0;
                decimal partialRefund = priceDifference < 0 ? Math.Abs(priceDifference) : 0;

                // STEP 4: Determine if approval is needed (customer gets money back)
                bool requiresApproval = partialRefund > 0;
                int? adjustmentId = null;

                // STEP 5: Create adjustment records
                if (additionalPayment > 0)
                {
                    var adjustment = new OrderAdjustment
                    {
                        OriginalOrderId = originalOrderId,
                        AdjustmentType = "EXCHANGE_PAYMENT",
                        Amount = additionalPayment,
                        Reason = $"Exchange - Customer paid extra: {reason}",
                        ReferenceNumber = $"EXCH-PAY-{DateTime.Now:yyyyMMddHHmmss}",
                        Status = "COMPLETED",
                        CreatedAt = DateTime.Now,
                        CreatedBy = userId  // ✅ FIXED: userId instead of cashierId
                    };
                    _db.OrderAdjustments.Add(adjustment);
                    await _db.SaveChangesAsync();
                    adjustmentId = adjustment.AdjustmentId;
                }

                if (partialRefund > 0)
                {
                    var adjustment = new OrderAdjustment
                    {
                        OriginalOrderId = originalOrderId,
                        AdjustmentType = "EXCHANGE_REFUND",
                        Amount = partialRefund,
                        Reason = $"Exchange - Refund to customer: {reason}",
                        ReferenceNumber = $"EXCH-REF-{DateTime.Now:yyyyMMddHHmmss}",
                        Status = requiresApproval ? "PENDING" : "COMPLETED",
                        CreatedAt = DateTime.Now,
                        CreatedBy = userId  // ✅ FIXED: userId instead of cashierId
                    };
                    _db.OrderAdjustments.Add(adjustment);
                    await _db.SaveChangesAsync();
                    adjustmentId = adjustment.AdjustmentId;

                    // If requires approval, create pending request
                    if (requiresApproval)
                    {
                        var pendingRequest = new PendingRequest
                        {
                            OrderId = originalOrderId,
                            RequestType = "EXCHANGE_REFUND",
                            Reason = $"Exchange requires refund of ₱{partialRefund}. Reason: {reason}",
                            ItemIds = string.Join(",", itemsToReturn.Select(x => x.OrderItemId)),
                            Status = "PENDING",
                            RequestedBy = userId,  // ✅ FIXED: userId instead of cashierId
                            RequestedAt = DateTime.Now
                        };
                        _db.PendingRequests.Add(pendingRequest);
                        await _db.SaveChangesAsync();
                    }
                }

                // STEP 6: Create exchange transaction (NO CashierId field)
                var exchange = new ExchangeTransaction
                {
                    OriginalOrderId = originalOrderId,
                    // CashierId = userId,  // ❌ REMOVED - wala na sa model
                    ExchangeDate = DateTime.Now,
                    Reason = reason,
                    TotalReturnedValue = totalReturnedValue,
                    TotalReplacementValue = totalReplacementValue,
                    PriceDifference = priceDifference,
                    AdditionalPayment = additionalPayment,
                    PartialRefund = partialRefund,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId  // ✅ This is the only user reference
                };
                _db.ExchangeTransactions.Add(exchange);
                await _db.SaveChangesAsync();

                // STEP 7: Update exchange items with exchange ID
                foreach (var item in processedReturnItems)
                {
                    item.ExchangeId = exchange.ExchangeId;
                    _db.ExchangeItems.Add(item);
                }
                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                // Build success message
                string msg = "Exchange completed successfully. ";
                if (additionalPayment > 0) msg += $"Customer paid additional ₱{additionalPayment}. ";
                if (partialRefund > 0 && requiresApproval) msg += $"₱{partialRefund} refund requires Owner approval. ";
                else if (partialRefund > 0) msg += $"₱{partialRefund} refunded to customer. ";

                return (true, msg, exchange.ExchangeId, requiresApproval);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Exchange failed: {ex.Message}", 0, false);
            }
        }

        /// <summary>
        /// Approve exchange refund
        /// </summary>
        public async Task<(bool success, string message)> ApproveExchangeRefundAsync(
            int requestId,
            int approverId)
        {
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var pendingRequest = await _db.PendingRequests
                    .Include(r => r.Order)
                    .FirstOrDefaultAsync(r => r.Id == requestId && r.RequestType == "EXCHANGE_REFUND");

                if (pendingRequest == null)
                    return (false, "Request not found");

                if (pendingRequest.Status != "PENDING")
                    return (false, "Request already processed");

                // Find the adjustment
                var adjustment = await _db.OrderAdjustments
                    .FirstOrDefaultAsync(a => a.OriginalOrderId == pendingRequest.OrderId &&
                                              a.AdjustmentType == "EXCHANGE_REFUND" &&
                                              a.Status == "PENDING");

                if (adjustment != null)
                {
                    adjustment.Status = "APPROVED";
                    adjustment.ApprovedBy = approverId;
                    adjustment.ApprovedAt = DateTime.Now;
                }

                pendingRequest.Status = "APPROVED";
                pendingRequest.ApprovedBy = approverId;
                pendingRequest.ApprovedAt = DateTime.Now;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, "Exchange refund has been approved");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error approving refund: {ex.Message}");
            }
        }

        #region Private Helpers

        private async Task ReturnIngredientsToStock(OrderItem orderItem, int quantityToReturn, string referenceNumber)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == orderItem.ProductSizeId);

            if (productSize == null) return;

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

        private async Task UpdateMainStock(int rawMaterialId)
        {
            var totalStock = await _db.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var material = await _db.RawMaterials.FindAsync(rawMaterialId);
            if (material != null)
                material.Stock = totalStock;
        }

        #endregion
    }

    public class ExchangeItemRequest
    {
        public int OrderItemId { get; set; }
        public int ProductSizeId { get; set; }
        public int Quantity { get; set; }
    }
}