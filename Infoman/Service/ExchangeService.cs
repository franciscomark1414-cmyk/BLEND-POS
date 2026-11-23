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
                    await transaction.RollbackAsync();
                    return (false, "Original order not found", 0);
                }

                if (originalOrder.Status != "COMPLETED")
                {
                    await transaction.RollbackAsync();
                    return (false, "Only completed orders can be exchanged", 0);
                }

                var returnedItemIds = itemsToReturn.Select(i => i.OrderItemId).ToList();
                var validItems = await _db.OrderItems
                    .Where(oi => oi.OrderId == originalOrderId && returnedItemIds.Contains(oi.OrderItemId))
                    .ToListAsync();

                if (validItems.Count != returnedItemIds.Count)
                {
                    await transaction.RollbackAsync();
                    return (false, "One or more items do not belong to the original order.", 0);
                }

                // ✅ Validate quantity: hindi lalampas sa binili, at hindi pwedeng duplicate ang item
                if (itemsToReturn.Select(i => i.OrderItemId).Distinct().Count() != itemsToReturn.Count)
                {
                    await transaction.RollbackAsync();
                    return (false, "Duplicate items in return list.", 0);
                }

                foreach (var req in itemsToReturn)
                {
                    var dbItem = validItems.First(v => v.OrderItemId == req.OrderItemId);
                    int alreadyExchanged = dbItem.QuantityExchanged ?? 0;
                    int available = dbItem.Quantity - alreadyExchanged;

                    if (req.Quantity <= 0)
                    {
                        await transaction.RollbackAsync();
                        return (false, "Return quantity must be at least 1.", 0);
                    }

                    if (req.Quantity > available)
                    {
                        await transaction.RollbackAsync();
                        return (false, $"Cannot return {req.Quantity}. Only {available} left to exchange for this item.", 0);
                    }
                }

                var allActiveItemIds = await _db.OrderItems
                    .Where(oi => oi.OrderId == originalOrderId && !oi.IsVoided && !oi.IsRefunded)
                    .Select(oi => oi.OrderItemId)
                    .ToListAsync();

                bool isFullExchange = allActiveItemIds.All(id => returnedItemIds.Contains(id));

                // ✅ Senior/PWD + Voucher combined factor
                decimal factor = (1 - originalOrder.DiscountPercent) * (1 - originalOrder.VoucherDiscountPercent / 100m);

                decimal originalAmount = 0;      // discounted value ng ibabalik
                decimal grossReturned = 0;       // gross value ng ibabalik
                decimal replacementAmount = 0;   // discounted value ng replacement
                decimal grossReplacement = 0;    // gross value ng replacement
                decimal totalWasteCost = 0;

                // STEP 1: Waste raw materials + compute return value (kasama add-ons)
                foreach (var returnItem in itemsToReturn)
                {
                    var orderItem = await _db.OrderItems
                        .Include(oi => oi.ProductSize)
                            .ThenInclude(ps => ps.RecipeItems)
                                .ThenInclude(ri => ri.RawMaterial)
                        .Include(oi => oi.Order)
                        .FirstOrDefaultAsync(oi => oi.OrderItemId == returnItem.OrderItemId);

                    if (orderItem == null) continue;

                    var returnedAddOns = await _db.OrderItemAddOns
                        .Include(a => a.AddOn)
                        .Where(a => a.OrderItemId == orderItem.OrderItemId)
                        .ToListAsync();

                    decimal addOnsPerUnit = returnedAddOns.Sum(a => a.Price * a.Quantity);
                    decimal itemGross = (orderItem.Price + addOnsPerUnit) * returnItem.Quantity;
                    grossReturned += itemGross;
                    originalAmount += Math.Round(itemGross * factor, 2);

                    // 1. Waste the main product recipe
                    foreach (var recipe in orderItem.ProductSize.RecipeItems)
                    {
                        decimal quantityWastedInBaseUnit = recipe.Quantity * returnItem.Quantity;
                        decimal quantityWastedInDisplayUnit = quantityWastedInBaseUnit;

                        if (recipe.RawMaterial.BaseUnit == "g" && recipe.RawMaterial.Unit == "kg")
                            quantityWastedInDisplayUnit = quantityWastedInBaseUnit / 1000;
                        else if (recipe.RawMaterial.BaseUnit == "mL" && recipe.RawMaterial.Unit == "L")
                            quantityWastedInDisplayUnit = quantityWastedInBaseUnit / 1000;

                        decimal activePrice = await _fifoService.GetActiveBatchPrice(recipe.RawMaterialId);
                        if (activePrice <= 0) activePrice = recipe.RawMaterial.Price;

                        decimal costWasted = quantityWastedInDisplayUnit * activePrice;
                        totalWasteCost += costWasted;

                        _db.WasteLogs.Add(new WasteLog
                        {
                            RawMaterialId = recipe.RawMaterialId,
                            Quantity = quantityWastedInBaseUnit,
                            Reason = WasteLog.WasteReason.CustomerCancel,
                            Date = DateTime.Now,
                            RecordedBy = userId.ToString(),
                            Notes = $"Exchange from Order #{originalOrder.OrderNumber}: {reason}",
                            TotalCost = costWasted
                        });

                        
                    }

                    // 2. Waste the Add-ons
                    foreach (var oldAddOn in returnedAddOns)
                    {
                        var addOnRecipe = await _db.AddOnRecipeItems
                            .Where(r => r.AddOnId == oldAddOn.AddOnId && !r.IsDeleted)
                            .Include(r => r.RawMaterial)
                            .ToListAsync();

                        foreach (var r in addOnRecipe)
                        {
                            decimal qtyToWaste = r.Quantity * oldAddOn.Quantity * returnItem.Quantity;
                            decimal qtyToWasteDisplay = qtyToWaste;

                            if (r.RawMaterial.BaseUnit == "g" && r.RawMaterial.Unit == "kg")
                                qtyToWasteDisplay = qtyToWaste / 1000;
                            else if (r.RawMaterial.BaseUnit == "mL" && r.RawMaterial.Unit == "L")
                                qtyToWasteDisplay = qtyToWaste / 1000;

                            decimal activePrice = await _fifoService.GetActiveBatchPrice(r.RawMaterialId);
                            if (activePrice <= 0) activePrice = r.RawMaterial.Price;

                            decimal costWasted = qtyToWasteDisplay * activePrice;
                            totalWasteCost += costWasted;

                            _db.WasteLogs.Add(new WasteLog
                            {
                                RawMaterialId = r.RawMaterialId,
                                Quantity = qtyToWaste,
                                Reason = WasteLog.WasteReason.CustomerCancel,
                                Date = DateTime.Now,
                                RecordedBy = userId.ToString(),
                                Notes = $"Exchange Add-on Waste from Order #{originalOrder.OrderNumber}: {reason}",
                                TotalCost = costWasted
                            });

                        }
                    }

                    orderItem.IsExchanged = true;
                    orderItem.QuantityExchanged = (orderItem.QuantityExchanged ?? 0) + returnItem.Quantity;
                }

                // STEP 2: Mark original order
                if (isFullExchange)
                {
                    originalOrder.ReturnStatus = "EXCHANGED";
                    originalOrder.Status = "EXCHANGED";
                    originalOrder.TotalAmount = 0;
                }
                else
                {
                    originalOrder.ReturnStatus = "PARTIALLY_EXCHANGED";
                    originalOrder.Status = "COMPLETED";

                    originalOrder.TotalAmount -= originalAmount;
                    originalOrder.OriginalTotalAmount -= grossReturned; // gross dapat
                }
                originalOrder.ExchangeDate = DateTime.Now;
                originalOrder.ExchangeReason = reason;

                // STEP 3: Replacement amount (kasama add-ons + discount/voucher)
                // ✅ Add-on price at name galing sa DB, hindi sa browser
                var replaceAddOnIds = itemsToReplace
                    .Where(i => i.AddOns != null)
                    .SelectMany(i => i.AddOns)
                    .Select(a => a.AddOnId)
                    .Distinct()
                    .ToList();

                var dbAddOns = await _db.AddOns
                    .Where(a => replaceAddOnIds.Contains(a.AddOnId) && a.IsActive && !a.IsDeleted)
                    .ToDictionaryAsync(a => a.AddOnId);

                foreach (var replaceItem in itemsToReplace)
                {
                    if (replaceItem.AddOns == null) continue;

                    foreach (var a in replaceItem.AddOns)
                    {
                        if (!dbAddOns.TryGetValue(a.AddOnId, out var dbAddOn))
                        {
                            await transaction.RollbackAsync();
                            return (false, "An add-on is no longer available.", 0);
                        }
                        a.Price = dbAddOn.Price;
                        a.AddOnName = dbAddOn.Name;
                    }
                }

                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize != null)
                    {
                        decimal addOnsPerUnit = replaceItem.AddOns?.Sum(a => a.Price * a.Quantity) ?? 0;
                        decimal gross = (productSize.Price + addOnsPerUnit) * replaceItem.Quantity;
                        grossReplacement += gross;
                        replacementAmount += Math.Round(gross * factor, 2);
                    }
                }

                // STEP 4: Adjustment
                decimal adjustmentAmount = replacementAmount - originalAmount;
                string adjustmentType = adjustmentAmount >= 0 ? "ADDITIONAL_PAYMENT" : "CHANGE";
                decimal absoluteAdjustment = Math.Abs(adjustmentAmount);

                // STEP 5: Create new order
                string newOrderStatus;
                if (adjustmentAmount <= 0)
                    newOrderStatus = "PENDING_BLENDING";
                else if (paymentMethodForDifference == "Cash")
                    newOrderStatus = "PENDING_BLENDING";
                else
                    newOrderStatus = "PENDING_PAYMENT";

                decimal manualDiscountAmount = Math.Round(grossReplacement * originalOrder.DiscountPercent, 2);
                decimal voucherDiscountAmount = Math.Round(
                    grossReplacement * (1 - originalOrder.DiscountPercent) * (originalOrder.VoucherDiscountPercent / 100m), 2);

                var newOrder = new Order
                {
                    OrderNumber = $"EX-{DateTime.Now:yyyyMMddHHmmss}",
                    Date = DateTime.Now,
                    TotalAmount = replacementAmount,
                    OriginalTotalAmount = grossReplacement,
                    Status = newOrderStatus,
                    PaymentMethod = (adjustmentAmount > 0 && !string.IsNullOrEmpty(paymentMethodForDifference))
                        ? paymentMethodForDifference
                        : "Store Credit",

                    // ✅ Ilipat ang discount at voucher ng original order
                    DiscountType = originalOrder.DiscountType,
                    DiscountPercent = originalOrder.DiscountPercent,
                    DiscountAmount = manualDiscountAmount,
                    VoucherCode = originalOrder.VoucherCode,
                    VoucherDiscountPercent = originalOrder.VoucherDiscountPercent,
                    VoucherDiscountAmount = voucherDiscountAmount,

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
                        if (replaceItem.AddOns != null && replaceItem.AddOns.Any())
                        {
                            foreach (var addOn in replaceItem.AddOns)
                            {
                                _db.OrderItemAddOns.Add(new OrderItemAddOn
                                {
                                    OrderItem = orderItem,
                                    AddOnId = addOn.AddOnId,
                                    AddOnName = addOn.AddOnName,
                                    Price = addOn.Price,
                                    Quantity = addOn.Quantity,
                                    ActualCost = 0
                                });
                            }
                        }
                    }
                }
                await _db.SaveChangesAsync();

                // STEP 7: Stock check lang (walang deduction; sa Start Blending mag-de-deduct)
                foreach (var replaceItem in itemsToReplace)
                {
                    var productSize = await _db.ProductSizes
                        .Include(ps => ps.RecipeItems)
                            .ThenInclude(ri => ri.RawMaterial)
                        .FirstOrDefaultAsync(ps => ps.ProductSizeId == replaceItem.ProductSizeId);

                    if (productSize == null) continue;

                    foreach (var recipe in productSize.RecipeItems.Where(r => !r.IsDeleted))
                    {
                        decimal needed = recipe.Quantity * replaceItem.Quantity;
                        decimal available = await _db.RawMaterialBatches
                            .Where(b => b.RawMaterialId == recipe.RawMaterialId
                                     && b.QuantityRemaining > 0
                                     && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                            .SumAsync(b => b.QuantityRemaining);

                        if (available < needed)
                        {
                            await transaction.RollbackAsync();
                            return (false, $"Insufficient stock for {recipe.RawMaterial.Name}.", 0);
                        }
                    }

                    foreach (var addOn in replaceItem.AddOns ?? new List<ExchangeAddOnDto>())
                    {
                        var addOnRecipes = await _db.AddOnRecipeItems
                            .Include(r => r.RawMaterial)
                            .Where(r => r.AddOnId == addOn.AddOnId && !r.IsDeleted)
                            .ToListAsync();

                        foreach (var r in addOnRecipes)
                        {
                            decimal needed = r.Quantity * replaceItem.Quantity * addOn.Quantity;
                            decimal available = await _db.RawMaterialBatches
                                .Where(b => b.RawMaterialId == r.RawMaterialId
                                         && b.QuantityRemaining > 0
                                         && (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= DateTime.Today))
                                .SumAsync(b => b.QuantityRemaining);

                            if (available < needed)
                            {
                                await transaction.RollbackAsync();
                                return (false, $"Insufficient stock for add-on {addOn.AddOnName}.", 0);
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
                // ✅ ISSUE 7: Create Payment record for additional payment
                // ✅ I-handle ang Additional Payment (Kung mas mahal ang replacement)
                if (adjustmentAmount > 0 && !string.IsNullOrEmpty(paymentMethodForDifference))
                {
                    _db.Payments.Add(new Payment
                    {
                        OrderId = newOrder.OrderId,
                        Amount = adjustmentAmount,
                        PaymentMethod = paymentMethodForDifference,
                        PaymentReference = $"EXPAY-{DateTime.Now:yyyyMMddHHmmss}-{newOrder.OrderId}",
                        Status = "PAID",
                        PaymentDate = DateTime.Now,
                        CreatedAt = DateTime.Now
                    });
                }
                // ✅ IDAGDAG ITO: I-handle ang Change/Refund (Kung mas mura ang replacement)
                else if (adjustmentAmount < 0)
                {
                    _db.Payments.Add(new Payment
                    {
                        OrderId = originalOrderId, // I-record sa ORIGINAL order kasi doon galing ang refund
                        Amount = Math.Abs(adjustmentAmount), // Positive amount pero irerecord natin as REFUNDED
                        PaymentMethod = "Store Credit", // O Cash kung binigay mo
                        PaymentReference = $"EXCHANGE-REFUND-{DateTime.Now:yyyyMMddHHmmss}",
                        Status = "REFUNDED",
                        PaymentDate = DateTime.Now,
                        CreatedAt = DateTime.Now
                    });
                }
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
                await transaction.RollbackAsync();
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                return (false, $"System error during exchange: {root.Message}", 0);
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

        // ✅ IDAGDAG ITO PARA SA ADD-ONS
        public List<ExchangeAddOnDto> AddOns { get; set; }
    }

    // ✅ IDAGDAG DIN ITO PARA SA DTO
    public class ExchangeAddOnDto
    {
        public int AddOnId { get; set; }
        public string AddOnName { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}