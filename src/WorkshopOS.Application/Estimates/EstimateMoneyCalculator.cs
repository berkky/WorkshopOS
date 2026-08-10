namespace WorkshopOS.Application.Estimates;

public static class EstimateMoneyCalculator
{
    public static decimal CalculateLineTotal(decimal quantity, decimal unitPrice) =>
        decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    public static decimal CalculateEstimateTotal(IReadOnlyList<(decimal Quantity, decimal UnitPrice)> items)
    {
        decimal total = 0;
        foreach (var item in items)
        {
            total += CalculateLineTotal(item.Quantity, item.UnitPrice);
        }

        return total;
    }
}
