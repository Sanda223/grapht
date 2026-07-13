using System;

namespace SampleApp.Legacy
{
    public class LegacyBilling
    {
        public decimal CalculateInvoice(decimal amount, decimal taxRate)
        {
            var tax = ComputeTax(amount, taxRate);
            return amount + tax;
        }

        private decimal ComputeTax(decimal amount, decimal taxRate)
        {
            return Math.Round(amount * taxRate, 2);
        }
    }
}
