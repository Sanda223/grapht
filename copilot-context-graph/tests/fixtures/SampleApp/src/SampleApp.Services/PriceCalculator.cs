namespace SampleApp.Services;

public static class PriceCalculator
{
    public static decimal ApplyDiscount(decimal total, int lineCount)
    {
        return lineCount >= 5 ? total * 0.9m : total;
    }
}
