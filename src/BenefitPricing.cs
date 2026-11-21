namespace PharmacyStock;

public static class BenefitPricing
{
    private const int MoneyDecimalPlaces = 2;
    private const decimal PensionerPaymentFactor = 0.5m;
    private const decimal FormerPensionerPaymentFactor = 0.7m;

    public static decimal CalculateCharge(Prescription prescription, Medicine medicine,
        int dispensedQuantity)
    {
        return Math.Round(medicine.Price * dispensedQuantity * PaymentFactor(prescription), MoneyDecimalPlaces);
    }

    private static decimal PaymentFactor(Prescription prescription)
    {
        if (prescription.Category == "PENSIONER")
            return PensionerFactor();
        if (prescription.Category == "DISABLED")
            return 0m;
        return 1m;
    }

    private static decimal PensionerFactor()
    {
        return IsFormerBenefitPeriod() ? FormerPensionerPaymentFactor : PensionerPaymentFactor;
    }

    private static bool IsFormerBenefitPeriod()
    {
        // TODO TD-04: Час залежить від DateTime.Today; старий тариф не покрито; до 12.12.2026.
        return DateTime.Today < new DateTime(2025, 4, 1);
    }
}
