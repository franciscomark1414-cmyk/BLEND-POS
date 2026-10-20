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

        public async Task<(bool success, string message, int newOrderId)> ProcessExchangeAsync(
      int originalOrderId,
      int userId,
      List<ExchangeItemRequest> itemsToReturn,
      List<ExchangeItemRequest> itemsToReplace,
      string reason,
      string? paymentMethodForDifference = null)
        {
            // ✅ SIMULAN ANG DATABASE TRANSACTION DITO
            using var transaction = await _db.Database.BeginTransactionAsync();

            try
            {
                var originalOrder = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.OrderId == originalOrderId);

                if (originalOrder == null)
                {
                    await transaction.RollbackAsync(); // Safe rollback kahit walang na-save
                    return (false, "Original order not found", 0);
                }

                if (originalOrder.Status != "COMPLETED")
                {
                    await transaction.RollbackAsync();
                    return (false, "Only completed orders can be exchanged", 0);
                }

                // Compute amounts
                decimal originalAmount = 0;
                decimal replacementAmount = 0;
                decimal totalWasteCost = 0;

                // STEP 1: Waste raw materials from original order
                foreach (var returnItem in itemsToReturn)
                {
                    var orderItem = await _db.OrderItems
                        .Include(oi => oi.ProductSize)
                            .ThenInclude(ps => ps.RecipeItems)
                                .ThenInclude(ri => ri.RawMaterial)
                        .Include(oi => oi.Order)
                        .FirstOrDefaultAsync(oi => oi.OrderItemId == returnItem.OrderItemId);

                    if (orderItem == null) continue;

                    decimal actualPricePerUnit = orderItem.Price;
                    if (orderItem.Order != null && orderItem.Order.DiscountPercent > 0)
                    {
                        actualPricePerUnit = orderItem.Price * (1 - orderItem.Order.DiscountPercent);
                    }

                    originalAmount += actualPricePerUnit * returnItem.Quantity;

                    foreach (var recipe in orderItem.ProductSize.RecipeItems)
                    {
                        decimal quantityWastedInBaseUnit = recipe.Quantity * returnItem.Quantity;
                        decimal quantityWastedInDisplayUnit = quantityWastedInBaseUnit;

                        if (recipe.RawMaterial.BaseUnit == "g" && recipe.RawMaterial.Unit == "kg")
                        {
                            quantityWastedInDisplayUnit = quantityWastedInBaseUnit / 1000;
                        }
                        else if (recipe.RawMaterial.BaseUnit == "mL" && recipe.RawMaterial.Unit == "L")
                        {
                            quantityWastedInDisplayUnit = quantityWastedInBaseUnit / 1000;
                        }

                        // ✅ KUNIN ANG ACTIVE BATCH PRICE (FIFO/FEFO)
                        decimal activePrice = await _fifoService.GetActiveBatchPrice(recipe.RawMaterialId);

                        // ✅ FALLBACK SA DEFAULT PRICE KUNG WALANG ACTIVE BATCH
                        if (activePrice <= 0)
                        {
                            activePrice = recipe.RawMaterial.Price;
                        }

                        // ✅ COMPUTE GAMIT ANG ACTIVE PRICE
                        decimal costWasted = quantityWastedInDisplayUnit * activePrice;
                        totalWasteCost += costWasted;

                        var waste = new WasteLog
                        {
                            RawMaterialId = recipe.RawMaterialId,
                            Quantity = quantityWastedInBaseUnit,
                            Reason = WasteLog.WasteReason.CustomerCancel,
                            Date = DateTime.Now,
                            RecordedBy = userId.ToString(),
                            Notes = $"Exchange from Order #{originalOrder.OrderNumber}: {reason}",
                            TotalCost = costWasted
                        };
                        _db.WasteLogs.Add(waste);

                        var material = await _db.RawMaterials.FindAsync(recipe.RawMaterialId);
                        if (material != null)
                        {
                            material.Stock -= quantityWastedInBaseUnit;
                        }
                    }

                    orderItem.IsExchanged = true;
                    orderItem.QuantityExchanged = returnItem.Quantity;
                }

                // STEP 2: Mark original order as EXCHANGED
                originalOrder.ReturnStatus = "EXCHANGED";
                originalOrder.Status = "EXCHANGED";
                originalOrder.ExchangeDate = DateTime.Now;
                originalOrder.ExchangeReason = reason;

                // STEP 3: Compute replacement amount
                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .Include(ps => ps.Product)
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize != null)
                    {
                        replacementAmount += productSize.Price * replaceItem.Quantity;
                    }
                }

                // STEP 4: Compute adjustment
                decimal adjustmentAmount = replacementAmount - originalAmount;
                string adjustmentType = adjustmentAmount >= 0 ? "ADDITIONAL_PAYMENT" : "CHANGE";
                decimal absoluteAdjustment = Math.Abs(adjustmentAmount);
                decimal customerPayable = adjustmentAmount > 0 ? adjustmentAmount : 0;

                // STEP 5: Create new order
                var newOrder = new Order
                {
                    OrderNumber = $"EX-{DateTime.Now:yyyyMMddHHmmss}",
                    Date = DateTime.Now,
                    TotalAmount = customerPayable,
                    OriginalTotalAmount = replacementAmount,
                    Status = "PENDING_PAYMENT",
                    PaymentMethod = (adjustmentAmount > 0 && !string.IsNullOrEmpty(paymentMethodForDifference))
                        ? paymentMethodForDifference
                        : "Store Credit",
                    DiscountType = "None",
                    DiscountPercent = 0,
                    DiscountAmount = 0,
                    ReceiptNumber = $"RC-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                    OriginalOrderId = originalOrderId,
                    StoreCreditApplied = originalAmount,
                    AdjustmentAmount = adjustmentAmount,
                    AdjustmentType = adjustmentType,
                    ReturnStatus = "ACTIVE",
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
                _db.Orders.Add(newOrder);
                await _db.SaveChangesAsync();

                // STEP 6: Add items to new order
                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .Include(ps => ps.Product)
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize != null)
                    {
                        var orderItem = new OrderItem
                        {
                            OrderId = newOrder.OrderId,
                            ProductId = productSize.ProductId,
                            ProductSizeId = replaceItem.ProductSizeId,
                            Quantity = replaceItem.Quantity,
                            Price = productSize.Price,
                            ActualCost = 0,
                            DispositionStatusId = 1
                        };
                        _db.OrderItems.Add(orderItem);
                    }
                }
                await _db.SaveChangesAsync();

                // STEP 7: Deduct raw materials for new order (FIFO)
                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .Include(ps => ps.RecipeItems)
                            .ThenInclude(ri => ri.RawMaterial)
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize != null)
                    {
                        foreach (var recipe in productSize.RecipeItems)
                        {
                            decimal quantityNeeded = recipe.Quantity * replaceItem.Quantity;

                            var (success, _, _) = await _fifoService.DeductStockWithCostAsync(
                                recipe.RawMaterialId,
                                quantityNeeded,
                                productSize.ProductId,
                                $"EXCHANGE-{newOrder.OrderNumber}"
                            );

                            if (!success)
                            {
                                // 🔴 PAG NAG-FAIL ANG STOCK, I-ROLLBACK LAHAT NG NA-SAVE SA STEP 1, 5, 6
                                await transaction.RollbackAsync();
                                return (false, $"Failed to deduct {recipe.RawMaterial.Name}. Insufficient stock.", 0);
                            }
                        }
                    }
                }

                // STEP 8: Create exchange transaction record
                var exchange = new ExchangeTransaction
                {
                    OriginalOrderId = originalOrderId,
                    NewOrderId = newOrder.OrderId,
                    ExchangeDate = DateTime.Now,
                    Reason = reason,
                    OriginalAmount = originalAmount,
                    ReplacementAmount = replacementAmount,
                    AdjustmentAmount = adjustmentAmount,
                    AdjustmentType = adjustmentType,
                    PaymentMethod = adjustmentAmount > 0 ? paymentMethodForDifference : null,
                    WasteCost = totalWasteCost,
                    TotalReturnedValue = originalAmount,
                    TotalReplacementValue = replacementAmount,
                    PriceDifference = adjustmentAmount,
                    AdditionalPayment = adjustmentAmount > 0 ? adjustmentAmount : 0,
                    PartialRefund = adjustmentAmount < 0 ? Math.Abs(adjustmentAmount) : 0,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
                _db.ExchangeTransactions.Add(exchange);
                await _db.SaveChangesAsync();

                // ✅ KAPAG NAKARATING DITO, IBIG SABIHIN TAGUMPAY ANG LAHAT. I-COMMIT NA NATIN.
                await transaction.CommitAsync();

                string message = adjustmentAmount > 0
                    ? $"Exchange completed. Customer paid additional ₱{absoluteAdjustment:F2}. New Order #{newOrder.OrderId} created."
                    : adjustmentAmount < 0
                        ? $"Exchange completed. Store credit of ₱{absoluteAdjustment:F2} applied. New Order #{newOrder.OrderId} created."
                        : $"Exchange completed. No additional payment. New Order #{newOrder.OrderId} created.";

                return (true, message, newOrder.OrderId);
            }
            catch (Exception ex)
            {
                // ✅ KAPAG MAY BIGLANG CRASH O ERROR, I-ROLLBACK LAHAT PARA WALANG KALAT
                await transaction.RollbackAsync();
                return (false, $"System error during exchange: {ex.Message}", 0);
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