using Infoman.Data;
using Infoman.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infoman.Service
{
    public class VoidRefundService
    {
        private readonly ConnectionDatabase _db;

        public VoidRefundService(ConnectionDatabase db)
        {
            _db = db;
        }

        private async Task<bool> IsItemPrepared(int orderItemId)
        {
            var orderItem = await _db.OrderItems
                .Include(oi => oi.Order)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == orderItemId);

            return orderItem?.Order?.Status == "PREPARING" ||
                   orderItem?.Order?.Status == "COMPLETED";
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

        // Services/VoidRefundService.cs - VoidItem method

        // Services/VoidRefundService.cs - VoidItem method

        // Services/VoidRefundService.cs - VoidItem method

        public async Task<(bool success, string message, bool needsOwner)> VoidItem(
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
                return (false, "Order item not found", false);

            bool isPrepared = orderItem.Order?.Status == "COMPLETED";
            bool isOwner = userRole == "Owner";

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

                return (false, "⚠️ This item has already been blended. Owner approval required to mark as waste.", true);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                if (!isPrepared)
                {
                    // Do nothing - no inventory change
                    System.Diagnostics.Debug.WriteLine($"Void item #{orderItemId} - NOT PREPARED. No inventory change.");
                }
                else
                {
                    await LogWasteForPreparedItem(orderItem, $"Void: {reason}", userId);
                }

                var voidLog = new VoidLog
                {
                    TransactionId = orderItem.OrderId,
                    ItemId = orderItem.OrderItemId,
                    VoidType = "item",
                    Reason = reason,
                    PreparedFlag = isPrepared,
                    PreparedStatus = isPrepared ? "PREPARED" : "NOT_PREPARED",
                    UserId = userId,
                    ApprovedBy = userId,
                    Timestamp = DateTime.Now
                };
                _db.VoidLogs.Add(voidLog);

                orderItem.IsVoided = true;
                orderItem.VoidReason = reason;

                // ✅ I-REMOVE ANG ITEM TOTAL SA ORDER TOTAL
                decimal itemTotal = orderItem.Price * orderItem.Quantity;
                orderItem.Order.TotalAmount -= itemTotal;

                // ✅ I-RECOMPUTE ANG DISCOUNT KUNG KUMPLETO NA ANG VOID
                await RecomputeOrderDiscount(orderItem.Order);

                var remainingItems = await _db.OrderItems
                    .Where(oi => oi.OrderId == orderItem.OrderId && !oi.IsVoided)
                    .CountAsync();

                if (remainingItems == 0)
                {
                    orderItem.Order.Status = "VOIDED";
                    orderItem.Order.VoidReason = "All items voided";
                    orderItem.Order.VoidedBy = userId;
                    orderItem.Order.VoidedAt = DateTime.Now;
                    orderItem.Order.DiscountAmount = 0;
                    orderItem.Order.DiscountPercent = 0;
                    orderItem.Order.DiscountType = "None";
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                string msg = isPrepared ?
                    $"✅ Prepared item '{orderItem.Product?.Name}' voided as WASTE." :
                    $"✅ Item '{orderItem.Product?.Name}' voided. (No inventory change - nothing was deducted)";
                return (true, msg, false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}", false);
            }
        }

        // ✅ IDAGDAG ANG HELPER METHOD NA ITO
        private async Task RecomputeOrderDiscount(Order order)
        {
            // Kunin ang lahat ng active items (hindi voided)
            var activeItems = await _db.OrderItems
                .Where(oi => oi.OrderId == order.OrderId && !oi.IsVoided && !oi.IsRefunded)
                .ToListAsync();

            // I-compute ang bagong subtotal
            decimal newSubtotal = activeItems.Sum(i => i.Price * i.Quantity);

            // I-apply ang discount percentage
            decimal newDiscountedTotal = newSubtotal * (1 - order.DiscountPercent);
            decimal newDiscountAmount = newSubtotal - newDiscountedTotal;

            // I-update ang order
            order.TotalAmount = newDiscountedTotal;
            order.DiscountAmount = newDiscountAmount;

            // Kung walang items, i-reset ang discount
            if (activeItems.Count == 0)
            {
                order.DiscountPercent = 0;
                order.DiscountAmount = 0;
                order.DiscountType = "None";
            }
        }  // Services/VoidRefundService.cs - VoidTransaction method

        public async Task<(bool success, string message, bool needsOwner)> VoidTransaction(
      int orderId, int userId, string userRole, string reason)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return (false, "Order not found", false);

            // ✅ CHECK KUNG COMPLETED NA (blended na)
            bool hasPrepared = order.Status == "COMPLETED";
            bool isOwner = userRole == "Owner";

            // Kapag prepared at hindi Owner, i-save sa PendingRequests
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

                return (false, "⚠️ Transaction contains blended items. Owner approval required to mark as waste.", true);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // Reload with full includes
                order = await _db.Orders
                    .Include(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                        .ThenInclude(p => p.Sizes)
                            .ThenInclude(ps => ps.RecipeItems)
                                .ThenInclude(ri => ri.RawMaterial)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                foreach (var item in order.Items.Where(i => !i.IsVoided))
                {
                    if (!hasPrepared)  // PENDING_PAYMENT - hindi pa nabe-blend
                    {
                        // Do nothing - no inventory change
                        System.Diagnostics.Debug.WriteLine($"Void transaction #{orderId} item #{item.OrderItemId} - NOT PREPARED. No inventory change.");
                    }
                    else  // COMPLETED - blended na
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
                        Timestamp = DateTime.Now
                    };
                    _db.VoidLogs.Add(voidLog);

                    item.IsVoided = true;
                    item.VoidReason = reason;
                }

                order.Status = "VOIDED";
                order.VoidReason = reason;
                order.VoidedBy = userId;
                order.VoidedAt = DateTime.Now;

                // ✅ I-RECOMPUTE ANG DISCOUNT (kung may natirang items, pero voided na lahat - dapat zero)
                await RecomputeOrderDiscount(order);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                string stockMsg = hasPrepared ? "Prepared items marked as waste." : "Transaction voided. No inventory change (nothing was deducted).";
                return (true, $"✅ Transaction voided. {stockMsg}", false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}", false);
            }
        }
        public async Task<(bool success, string message, bool needsOwner)> ProcessRefund(
     int orderId, List<int> itemIds, string reason, int userId, string userRole)
        {
            var isOwner = userRole == "Owner";

            // Kapag Staff, i-save muna sa PendingRequests
            if (!isOwner)
            {
                var pendingRequest = new PendingRequest
                {
                    OrderId = orderId,
                    ItemIds = itemIds != null ? string.Join(",", itemIds) : "",
                    RequestType = "REFUND",
                    Reason = reason,
                    RequestedBy = userId,
                    Status = "PENDING",
                    RequestedAt = DateTime.Now
                };
                _db.PendingRequests.Add(pendingRequest);
                await _db.SaveChangesAsync();

                return (false, "⚠️ Refund request sent to Owner for approval. Please wait for Owner to process.", true);
            }

            // Kapag Owner, diretso process
            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                        .ThenInclude(oi => oi.Product)
                        .ThenInclude(p => p.Sizes)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);

                if (order == null)
                    return (false, "Order not found", false);

                if (order.Status != "COMPLETED")
                    return (false, "Only completed orders can be refunded", false);

                decimal refundAmount = 0;

                foreach (var itemId in itemIds)
                {
                    var item = order.Items.FirstOrDefault(i => i.OrderItemId == itemId && !i.IsRefunded);
                    if (item == null) continue;

                    refundAmount += item.Price * item.Quantity;

                    // Log as waste dahil prepared na ang item
                    await LogWasteForPreparedItem(item, $"Refund: {reason}", userId);

                    item.IsRefunded = true;
                    item.RefundReason = reason;
                }

                if (refundAmount == 0)
                    return (false, "No valid items to refund", false);

                var refund = new Refund
                {
                    TransactionId = orderId,
                    Amount = refundAmount,
                    Reason = reason,
                    ProcessedBy = userId,
                    ApprovedBy = userId,
                    Timestamp = DateTime.Now,
                    RefundReference = $"RFD-{DateTime.Now:yyyyMMddHHmmss}"
                };
                _db.Refunds.Add(refund);

                order.TotalAmount -= refundAmount;

                if (order.Items.All(i => i.IsRefunded || i.IsVoided))
                {
                    order.Status = "REFUNDED";
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return (true, $"✅ Refund of ₱{refundAmount:N2} processed. Items marked as waste.", false);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return (false, $"Error: {ex.Message}", false);
            }
        }

        public async Task<object> GetOrderDetails(int orderId)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.Product)
                .Include(o => o.Items)
                    .ThenInclude(oi => oi.ProductSize)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return null;

            // ✅ HUWAG IPAKITA ANG VOIDED O REFUNDED ORDERS
            if (order.Status == "VOIDED" || order.Status == "REFUNDED")
                return null;

            return new
            {
                order.OrderId,
                order.OrderNumber,
                order.Status,
                order.TotalAmount,
                DiscountType = string.IsNullOrEmpty(order.DiscountType) ? "None" : order.DiscountType,
                DiscountPercent = order.DiscountPercent,  // Hindi na kailangan ng ?? dahil decimal ito
                order.PaymentMethod,
                order.Date,
                Items = order.Items
                    .Where(i => !i.IsVoided && !i.IsRefunded)
                    .Select(oi => new
                    {
                        oi.OrderItemId,
                        ProductId = oi.ProductId,  // Hindi na kailangan ng ?? 0
                        ProductSizeId = oi.ProductSizeId,  // ✅ ITO ANG SUSI!
                        ProductName = oi.Product != null ? oi.Product.Name : "Unknown Product",
                        SizeName = oi.ProductSize != null ? oi.ProductSize.SizeName : "Regular",
                        oi.Quantity,
                        oi.Price,
                        Subtotal = oi.Price * oi.Quantity,
                        oi.IsVoided,
                        oi.IsRefunded
                    })
            };
        }

        public async Task<List<object>> GetPendingOrders()
        {
            return await _db.Orders
                .Where(o => (o.Status == "PENDING_PAYMENT" || o.Status == "COMPLETED")
                            && o.Status != "VOIDED"  // ✅ HUWAG ISAMA ANG VOIDED
                            && o.Status != "REFUNDED"  // ✅ HUWAG ISAMA ANG REFUNDED
                            && o.Items.Any(i => !i.IsVoided && !i.IsRefunded))  // ✅ MAY ACTIVE ITEMS PA
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
        }
        public async Task<List<object>> GetCompletedOrders()
        {
            return await _db.Orders
                .Where(o => o.Status == "COMPLETED"
                            && o.Status != "VOIDED"
                            && o.Status != "REFUNDED"
                            && o.Items.Any(i => !i.IsRefunded && !i.IsVoided))
                .OrderByDescending(o => o.Date)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderNumber,
                    o.Status,
                    o.TotalAmount,
                    o.Date,
                    o.PaymentMethod,
                    ItemCount = o.Items.Count(i => !i.IsRefunded && !i.IsVoided)
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

            var refunds = await _db.Refunds
                .Include(r => r.Transaction)
                .Where(r => r.Timestamp.Date >= startDate.Date && r.Timestamp.Date <= endDate.Date)
                .ToListAsync();

            var wasteLogs = await _db.WasteLogs
                .Include(w => w.RawMaterial)
                .Where(w => w.Date >= startDate.Date && w.Date <= endDate.Date)
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
                    Amount = v.Item != null ? (v.Item.Price * v.Item.Quantity) : 0
                }),
                Refunds = refunds.Select(r => new
                {
                    r.RefundId,
                    OrderNumber = r.Transaction?.OrderNumber,
                    r.Amount,
                    r.Reason,
                    r.Timestamp
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
                TotalVoidAmount = voidLogs.Sum(v => v.Item != null ? (v.Item.Price * v.Item.Quantity) : 0),
                TotalRefundAmount = refunds.Sum(r => r.Amount),
                TotalWasteCost = wasteLogs.Sum(w => w.Quantity * (w.RawMaterial?.Price ?? 0)),
                VoidCount = voidLogs.Count,
                RefundCount = refunds.Count
            };
        }
    }
}