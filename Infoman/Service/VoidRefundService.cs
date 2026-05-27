using Infoman.Constants;
using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Service
{
    /// <summary>
    /// VOID ONLY Service - For voiding items/transactions BEFORE completion
    /// NOW WITH CASH REFUND SUPPORT
    /// </summary>
    public class VoidRefundService
    {
        private readonly ConnectionDatabase _db;

        public VoidRefundService(ConnectionDatabase db)
        {
            _db = db;
        }

        #region Private Helpers

        private async Task<bool> IsItemPrepared(int orderItemId)
        {
            var orderItem = await _db.OrderItems
                .Include(oi => oi.Order)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == orderItemId);

            return orderItem?.Order?.Status == OrderStatus.PREPARING ||
                   orderItem?.Order?.Status == OrderStatus.COMPLETED;
        }

        private async Task LogInventoryMovement(int rawMaterialId, int? productId, decimal quantity,
            string movementType, string referenceId, string details, int? userId, decimal unitPrice = 0)
        {
            var movement = new InventoryMovement
            {
                RawMaterialId = rawMaterialId,
                ProductId = productId,
                Quantity = quantity,
                MovementType = movementType,
                ReferenceId = referenceId,
                Timestamp = DateTime.Now,
                Details = details,
                UserId = userId,
                UnitPrice = unitPrice
            };
            _db.InventoryMovements.Add(movement);
            await _db.SaveChangesAsync();
        }

        private async Task ReturnIngredientsToStock(OrderItem orderItem, int userId)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == orderItem.ProductSizeId);

            if (productSize?.RecipeItems == null) return;

            foreach (var recipe in productSize.RecipeItems)
            {
                var rawMaterial = recipe.RawMaterial;
                if (rawMaterial == null) continue;

                decimal quantityToReturn = recipe.Quantity * orderItem.Quantity;

                if (rawMaterial.BaseUnit == "g" || rawMaterial.BaseUnit == "mL")
                {
                    quantityToReturn = quantityToReturn / 1000;
                }

                var batch = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                    .OrderByDescending(b => b.ReceivedDate)
                    .FirstOrDefaultAsync();

                if (batch != null)
                {
                    batch.QuantityRemaining += quantityToReturn;

                    await LogInventoryMovement(
                        rawMaterialId: recipe.RawMaterialId,
                        productId: orderItem.ProductId,
                        quantity: quantityToReturn,
                        movementType: "VOID_RETURN",
                        referenceId: orderItem.Order?.OrderNumber,
                        details: $"Returned from voided order item - {orderItem.Product?.Name} (Not prepared)",
                        userId: userId,
                        unitPrice: batch.UnitPrice
                    );
                }
            }

            foreach (var recipe in productSize.RecipeItems)
            {
                await UpdateMainStock(recipe.RawMaterialId);
            }
        }

        private async Task LogWasteForPreparedItem(OrderItem orderItem, string reason, int userId)
        {
            var productSize = await _db.ProductSizes
                .Include(ps => ps.RecipeItems)
                    .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(ps => ps.ProductSizeId == orderItem.ProductSizeId);

            if (productSize?.RecipeItems == null) return;

            foreach (var recipe in productSize.RecipeItems)
            {
                var rawMaterial = recipe.RawMaterial;
                if (rawMaterial == null) continue;

                decimal wasteQuantity = recipe.Quantity * orderItem.Quantity;

                decimal displayQuantity = wasteQuantity;
                if (rawMaterial.BaseUnit == "g" || rawMaterial.BaseUnit == "mL")
                {
                    displayQuantity = wasteQuantity / 1000;
                }

                var batch = await _db.RawMaterialBatches
                    .Where(b => b.RawMaterialId == recipe.RawMaterialId && b.QuantityRemaining > 0)
                    .OrderBy(b => b.ExpiryDate ?? DateTime.MaxValue)
                    .FirstOrDefaultAsync();

                decimal unitPrice = batch?.UnitPrice ?? rawMaterial.Price;

                var wasteReason = reason.Contains("void") ? WasteLog.WasteReason.BlendedButCancelled : WasteLog.WasteReason.CustomerCancel;

                var wasteLog = new WasteLog
                {
                    RawMaterialId = recipe.RawMaterialId,
                    ProductId = orderItem.ProductId,
                    Quantity = displayQuantity,
                    Reason = wasteReason,
                    Date = DateTime.Today,
                    RecordedBy = userId.ToString(),
                    Notes = $"From {orderItem.Order?.OrderNumber}: {reason}"
                };
                _db.WasteLogs.Add(wasteLog);

                await LogInventoryMovement(
                    rawMaterialId: recipe.RawMaterialId,
                    productId: orderItem.ProductId,
                    quantity: wasteQuantity,
                    movementType: "WASTE",
                    referenceId: orderItem.Order?.OrderNumber,
                    details: $"Waste from {reason} - {orderItem.Product?.Name} (Already prepared/blended)",
                    userId: userId,
                    unitPrice: unitPrice
                );
            }
        }

        private async Task UpdateMainStock(int rawMaterialId)
        {
            var totalStock = await _db.RawMaterialBatches
                .Where(b => b.RawMaterialId == rawMaterialId)
                .SumAsync(b => (decimal?)b.QuantityRemaining) ?? 0;

            var material = await _db.RawMaterials.FindAsync(rawMaterialId);
            if (material != null)
            {
                material.Stock = totalStock;
            }
        }

        private async Task RecomputeOrderDiscount(Order order)
        {
            var activeItems = await _db.OrderItems
                .Where(oi => oi.OrderId == order.OrderId &&
                             oi.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                .ToListAsync();

            decimal newSubtotal = activeItems.Sum(i => i.Price * i.Quantity);
            decimal newDiscountedTotal = newSubtotal * (1 - order.DiscountPercent);
            decimal newDiscountAmount = newSubtotal - newDiscountedTotal;

            order.TotalAmount = newDiscountedTotal;
            order.DiscountAmount = newDiscountAmount;

            if (activeItems.Count == 0)
            {
                order.DiscountPercent = 0;
                order.DiscountAmount = 0;
                order.DiscountType = "None";
            }
        }

        private string GenerateRefundReference()
        {
            return $"REF-{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";
        }

        #endregion

        #region Void Methods (UPDATED WITH CASH REFUND)

        /// <summary>
        /// Void a single item BEFORE blending. Returns refund amount to customer.
        /// </summary>
        public async Task<(bool success, string message, bool needsOwner, decimal refundAmount, decimal newOrderTotal, List<object> updatedItems)> VoidItem(
            int orderItemId, int userId, string userRole, string reason)
        {
            var orderItem = await _db.OrderItems
                .Include(oi => oi.Order)
                .Include(oi => oi.Product)
                .Include(oi => oi.ProductSize)
                    .ThenInclude(ps => ps.RecipeItems)
                        .ThenInclude(ri => ri.RawMaterial)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == orderItemId);

            if (orderItem == null)
                return (false, "Order item not found", false, 0, 0, null);

            bool isPrepared = orderItem.Order?.Status == OrderStatus.COMPLETED;
            bool isOwner = userRole == "Owner";

            // If prepared and not owner, request approval (use existing refund service)
            if (isPrepared && !isOwner)
            {
                var pendingRequest = new PendingRequest
                {
                    OrderId = orderItem.OrderId,
                    ItemIds = orderItemId.ToString(),
                    RequestType = "VOID",
                    Reason = reason,
                    RequestedBy = userId,
                    Status = "PENDING",
                    RequestedAt = DateTime.Now
                };
                _db.PendingRequests.Add(pendingRequest);
                await _db.SaveChangesAsync();

                return (false, "⚠️ This item has already been blended. Owner approval required to mark as waste.", true, 0, 0, null);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = orderItem.Order;
                decimal refundAmount = orderItem.Price * orderItem.Quantity;

                // Apply discount proportionally if order has discount
                if (order.DiscountPercent > 0)
                {
                    refundAmount = refundAmount * (1 - order.DiscountPercent);
                }

                // 1. Handle inventory (return to stock since not prepared)
                if (!isPrepared)
                {
                    await ReturnIngredientsToStock(orderItem, userId);
                }
                else
                {
                    await LogWasteForPreparedItem(orderItem, $"Void: {reason}", userId);
                }

                // 2. Create OrderAdjustment for REFUND
                var adjustment = new OrderAdjustment
                {
                    OriginalOrderId = order.OrderId,
                    AdjustmentType = "VOID_REFUND",
                    Amount = refundAmount,
                    Reason = reason,
                    ReferenceNumber = GenerateRefundReference(),
                    Status = "COMPLETED",
                    ApprovedBy = userId,
                    ApprovedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
                _db.OrderAdjustments.Add(adjustment);

                // 3. Create VoidLog with refund amount
                var voidLog = new VoidLog
                {
                    TransactionId = order.OrderId,
                    ItemId = orderItem.OrderItemId,
                    VoidType = "item",
                    Reason = reason,
                    PreparedFlag = isPrepared,
                    PreparedStatus = isPrepared ? "PREPARED" : "NOT_PREPARED",
                    UserId = userId,
                    ApprovedBy = userId,
                    Timestamp = DateTime.Now,
                    RefundAmount = refundAmount,
                    IsRefundProcessed = true
                };
                _db.VoidLogs.Add(voidLog);

                var refundPayment = new Payment
                {
                    OrderId = order.OrderId,
                    Amount = -refundAmount,  // Negative amount para malaman na refund
                    PaymentMethod = order.PaymentMethod,  // Same payment method
                    PaymentReference = $"REFUND-{adjustment.ReferenceNumber}",
                    Status = "REFUNDED",
                    PaymentDate = DateTime.Now,
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(refundPayment);

                // 4. Update item disposition
                orderItem.DispositionStatusId = ItemDispositionConstants.VOIDED;
                orderItem.DispositionReason = reason;
                orderItem.DispositionProcessedAt = DateTime.Now;
                orderItem.DispositionProcessedBy = userId;

                // 5. Update order total
                decimal oldTotal = order.TotalAmount;
                decimal newTotal = oldTotal - refundAmount;
                if (newTotal < 0) newTotal = 0;
                order.TotalAmount = newTotal;
                order.TotalRefundAmount = order.TotalRefundAmount + refundAmount;

                // 6. Recompute discount if needed
                await RecomputeOrderDiscount(order);

                // 7. Check if order is fully voided
                var remainingItems = await _db.OrderItems
                    .Where(oi => oi.OrderId == order.OrderId &&
                                 oi.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                    .CountAsync();

                if (remainingItems == 0)
                {
                    order.Status = OrderStatus.VOIDED;
                    order.VoidReason = "All items voided - Full refund given";
                    order.VoidedBy = userId;
                    order.VoidedAt = DateTime.Now;
                }
                else
                {
                    //order.Status = OrderStatus.PARTIALLY_REFUNDED;
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // 8. Get updated active items for receipt display
                var updatedItems = await GetActiveOrderItems(order.OrderId);

                string msg = isPrepared ?
                    $"✅ Item '{orderItem.Product?.Name}' voided as WASTE. Refund: ₱{refundAmount:F2}" :
                    $"✅ Item '{orderItem.Product?.Name}' voided. Refund: ₱{refundAmount:F2} (Cash back to customer)";

                return (true, msg, false, refundAmount, newTotal, updatedItems);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}", false, 0, 0, null);
            }
        }

        /// <summary>
        /// Void entire transaction BEFORE blending. Returns full refund to customer.
        /// </summary>
        public async Task<(bool success, string message, bool needsOwner, decimal refundAmount, List<object> updatedItems)> VoidTransaction(
            int orderId, int userId, string userRole, string reason)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return (false, "Order not found", false, 0, null);

            bool hasPrepared = order.Status == OrderStatus.COMPLETED;
            bool isOwner = userRole == "Owner";

            if (hasPrepared && !isOwner)
            {
                var pendingRequest = new PendingRequest
                {
                    OrderId = orderId,
                    ItemIds = "",
                    RequestType = "VOID",
                    Reason = reason,
                    RequestedBy = userId,
                    Status = "PENDING",
                    RequestedAt = DateTime.Now
                };
                _db.PendingRequests.Add(pendingRequest);
                await _db.SaveChangesAsync();

                return (false, "⚠️ Transaction contains blended items. Owner approval required to mark as waste.", true, 0, null);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                order = await _db.Orders
                    .Include(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                        .ThenInclude(p => p.Sizes)
                            .ThenInclude(ps => ps.RecipeItems)
                                .ThenInclude(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                decimal refundAmount = order.TotalAmount;

                // 1. Create OrderAdjustment for full refund
                var adjustment = new OrderAdjustment
                {
                    OriginalOrderId = order.OrderId,
                    AdjustmentType = "VOID_REFUND",
                    Amount = refundAmount,
                    Reason = reason,
                    ReferenceNumber = GenerateRefundReference(),
                    Status = "COMPLETED",
                    ApprovedBy = userId,
                    ApprovedAt = DateTime.Now,
                    CreatedAt = DateTime.Now,
                    CreatedBy = userId
                };
                _db.OrderAdjustments.Add(adjustment);

                var refundPayment = new Payment
                {
                    OrderId = order.OrderId,
                    Amount = -refundAmount,  // Negative amount para malaman na refund
                    PaymentMethod = order.PaymentMethod,  // Same payment method
                    PaymentReference = $"REFUND-{adjustment.ReferenceNumber}",
                    Status = "REFUNDED",
                    PaymentDate = DateTime.Now,
                    CreatedAt = DateTime.Now
                };
                _db.Payments.Add(refundPayment);

                // 2. Process each active item
                foreach (var item in order.Items.Where(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE))
                {
                    if (!hasPrepared)
                    {
                        await ReturnIngredientsToStock(item, userId);
                    }
                    else
                    {
                        await LogWasteForPreparedItem(item, $"Transaction voided: {reason}", userId);
                    }

                    var voidLog = new VoidLog
                    {
                        TransactionId = orderId,
                        ItemId = item.OrderItemId,
                        VoidType = "transaction",
                        Reason = reason,
                        PreparedFlag = hasPrepared,
                        PreparedStatus = hasPrepared ? "PREPARED" : "NOT_PREPARED",
                        UserId = userId,
                        ApprovedBy = userId,
                        Timestamp = DateTime.Now,
                        RefundAmount = item.Price * item.Quantity,
                        IsRefundProcessed = true
                    };
                    _db.VoidLogs.Add(voidLog);

                    item.DispositionStatusId = ItemDispositionConstants.VOIDED;
                    item.DispositionReason = reason;
                    item.DispositionProcessedAt = DateTime.Now;
                    item.DispositionProcessedBy = userId;
                }

                // 3. Update order
                order.Status = OrderStatus.VOIDED;
                order.VoidReason = reason;
                order.VoidedBy = userId;
                order.VoidedAt = DateTime.Now;
                order.TotalAmount = 0;
                order.TotalRefundAmount = refundAmount;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // 4. Get empty items list for receipt
                var updatedItems = new List<object>();

                string stockMsg = hasPrepared ? "Prepared items marked as waste." : "Inventory returned to stock.";
                string msg = $"✅ Transaction voided. Full refund: ₱{refundAmount:F2} (Cash back to customer). {stockMsg}";

                return (true, msg, false, refundAmount, updatedItems);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}", false, 0, null);
            }
        }

        /// <summary>
        /// Get active (non-voided, non-refunded) items for an order
        /// </summary>
        private async Task<List<object>> GetActiveOrderItems(int orderId)
        {
            var items = await _db.OrderItems
                .Include(oi => oi.Product)
                .Include(oi => oi.ProductSize)
                .Where(oi => oi.OrderId == orderId &&
                             oi.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                .Select(oi => new
                {
                    oi.OrderItemId,
                    ProductName = oi.Product != null ? oi.Product.Name : "Unknown",
                    SizeName = oi.ProductSize != null ? oi.ProductSize.SizeName : "Regular",
                    oi.Quantity,
                    oi.Price,
                    Subtotal = oi.Price * oi.Quantity
                })
                .ToListAsync<object>();

            return items;
        }

        #endregion

        #region Query Methods (Existing - No Changes)

        public async Task<object> GetOrderDetails(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return null;

            // Get ALL items - include voided/refunded for complete history
            var allItems = order.Items.Select(oi => new
            {
                oi.OrderItemId,
                ProductId = oi.ProductId,
                ProductSizeId = oi.ProductSizeId,
                ProductName = oi.Product != null ? oi.Product.Name : "Unknown Product",
                SizeName = oi.ProductSize != null ? oi.ProductSize.SizeName : "Regular",
                oi.Quantity,
                oi.Price,
                Subtotal = oi.Price * oi.Quantity,
                IsVoided = oi.DispositionStatusId == ItemDispositionConstants.VOIDED,
                IsRefunded = oi.DispositionStatusId == ItemDispositionConstants.REFUNDED,
                IsExchanged = oi.DispositionStatusId == ItemDispositionConstants.EXCHANGED,
                DispositionStatusId = oi.DispositionStatusId
            }).ToList();

            // Calculate total refund amount from adjustments
            decimal totalRefundAmount = await _db.OrderAdjustments
                .Where(a => a.OriginalOrderId == orderId &&
                           (a.AdjustmentType == "VOID_REFUND" || a.AdjustmentType == "REFUND") &&
                           a.Status == "COMPLETED")
                .SumAsync(a => a.Amount);

            return new
            {
                order.OrderId,
                order.OrderNumber,
                order.Status,
                order.TotalAmount,
                TotalRefundAmount = totalRefundAmount,
                DiscountType = string.IsNullOrEmpty(order.DiscountType) ? "None" : order.DiscountType,
                DiscountPercent = order.DiscountPercent,
                order.PaymentMethod,
                order.Date,
                Items = allItems
            };
        }

        public async Task<List<object>> GetPendingOrders()
        {
            var today = DateTime.Today;

            var result = await _db.Orders
                .Where(o => o.Status == "PENDING_PAYMENT"
                            && o.Date.Date == today.Date
                            && o.Items.Any(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE))
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    o.PaymentMethod
                })
                .ToListAsync<object>();

            return result;
        }

        public async Task<List<object>> GetCompletedOrders()
        {
            var today = DateTime.Today;

            return await _db.Orders
                .Where(o => o.Status == "COMPLETED"
                            && o.Date.Date == today.Date
                            && o.Items.Any(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE))
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    o.PaymentMethod,
                    ItemCount = o.Items.Count(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                })
                .ToListAsync<object>();
        }

        public async Task<List<object>> GetPendingOrdersByDate(DateTime targetDate)
        {
            var result = await _db.Orders
                .Where(o => o.Status == "PENDING_PAYMENT"
                            && o.Status != "VOIDED"
                            && o.Status != "REFUNDED"
                            && o.Date.Date == targetDate.Date
                            && o.Items.Any(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE))
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    o.PaymentMethod
                })
                .ToListAsync<object>();

            return result;
        }

        public async Task<List<object>> GetCompletedOrdersByDate(DateTime targetDate)
        {
            return await _db.Orders
                .Where(o => o.Status == "COMPLETED"
                            && o.Status != "VOIDED"
                            && o.Status != "REFUNDED"
                            && o.Date.Date == targetDate.Date
                            && o.Items.Any(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE))
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    o.PaymentMethod,
                    ItemCount = o.Items.Count(i => i.DispositionStatusId == ItemDispositionConstants.AVAILABLE)
                })
                .ToListAsync<object>();
        }

        public async Task<object> GetReports(DateTime startDate, DateTime endDate)
        {
            var voidLogs = await _db.VoidLogs
                .Include(v => v.Transaction)
                .Include(v => v.Item)
                    .ThenInclude(i => i.Product)
                .Include(v => v.User)
                .Where(v => v.Timestamp.Date >= startDate.Date && v.Timestamp.Date <= endDate.Date)
                .ToListAsync();

            var wasteLogs = await _db.WasteLogs
                .Include(w => w.RawMaterial)
                .Where(w => w.Date >= startDate.Date && w.Date <= endDate.Date)
                .ToListAsync();

            var refundAdjustments = await _db.OrderAdjustments
                .Where(a => (a.AdjustmentType == "VOID_REFUND" || a.AdjustmentType == "REFUND") &&
                            a.CreatedAt.Date >= startDate.Date && a.CreatedAt.Date <= endDate.Date &&
                            a.Status == "COMPLETED")
                .ToListAsync();

            return new
            {
                VoidLogs = voidLogs.Select(v => new
                {
                    v.VoidLogId,
                    OrderNumber = v.Transaction?.OrderNumber,
                    ProductName = v.Item?.Product?.Name,
                    v.VoidType,
                    v.Reason,
                    v.PreparedFlag,
                    UserName = v.User?.Username,
                    v.Timestamp,
                    Amount = v.Item != null ? (v.Item.Price * v.Item.Quantity) : 0,
                    v.RefundAmount,
                    v.IsRefundProcessed
                }),
                WasteLogs = wasteLogs.Select(w => new
                {
                    w.WasteLogId,
                    MaterialName = w.RawMaterial?.Name,
                    w.Quantity,
                    Reason = w.Reason.ToString(),
                    w.Date,
                    TotalCost = w.Quantity * (w.RawMaterial?.Price ?? 0)
                }),
                RefundAdjustments = refundAdjustments.Select(a => new
                {
                    a.AdjustmentId,
                    a.OriginalOrderId,
                    a.AdjustmentType,
                    a.Amount,
                    a.Reason,
                    a.ReferenceNumber,
                    a.CreatedAt
                }),
                TotalVoidAmount = voidLogs.Sum(v => v.Item != null ? (v.Item.Price * v.Item.Quantity) : 0),
                TotalRefundAmount = refundAdjustments.Sum(a => a.Amount),
                TotalWasteCost = wasteLogs.Sum(w => w.Quantity * (w.RawMaterial?.Price ?? 0)),
                VoidCount = voidLogs.Count
            };
        }

        #endregion
    }
}