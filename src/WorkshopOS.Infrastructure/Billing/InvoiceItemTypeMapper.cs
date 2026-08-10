using WorkshopOS.Domain.Billing;
using WorkshopOS.Domain.Estimates;

namespace WorkshopOS.Infrastructure.Billing;

internal static class InvoiceItemTypeMapper
{
    public static InvoiceItemType FromEstimateItemType(EstimateItemType estimateItemType) =>
        estimateItemType switch
        {
            EstimateItemType.Part => InvoiceItemType.Part,
            EstimateItemType.Service => InvoiceItemType.Service,
            EstimateItemType.Labor => InvoiceItemType.Other,
            EstimateItemType.Fee => InvoiceItemType.Other,
            _ => InvoiceItemType.Other,
        };
}
