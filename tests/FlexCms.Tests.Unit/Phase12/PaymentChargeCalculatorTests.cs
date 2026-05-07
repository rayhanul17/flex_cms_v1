using FlexCms.Framework.Payments;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

/// <summary>
/// Locks down the Forward / Backward charge math: each scenario is a real
/// shape we expect to see in production (bKash 1.85% + 15% VAT, SSLCommerz
/// 2.5%, with extras + fixed fees).
/// </summary>
public class PaymentChargeCalculatorTests
{
    private static readonly PaymentChargeCalculator Calc = new();

    [Fact]
    public void Forward_customer_pays_charge_plus_vat_plus_extra_merchant_gets_full_order()
    {
        // bKash standard: 1.85% + 15% VAT, no fixed/extra
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 1.85m,
            VatPercent = 15m
        };
        var c = Calc.Calculate(1000m, cfg);

        Assert.Equal(1000m, c.OrderAmount);
        Assert.Equal(18.50m, c.GatewayCharge);     // 1000 * 1.85%
        Assert.Equal(2.78m, c.Vat);                 // 18.50 * 15% = 2.775 → banker's = 2.78
        Assert.Equal(0m, c.ExtraCharge);
        Assert.Equal(1021.28m, c.CustomerPays);     // 1000 + 18.50 + 2.78
        Assert.Equal(1000m, c.MerchantReceives);
        Assert.Equal(ChargeBearer.Forward, c.Bearer);
    }

    [Fact]
    public void Backward_customer_pays_order_only_merchant_absorbs_charge()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 1.85m,
            VatPercent = 15m
        };
        var c = Calc.Calculate(1000m, cfg);

        Assert.Equal(1000m, c.CustomerPays);
        Assert.Equal(978.72m, c.MerchantReceives); // 1000 - 18.50 - 2.78
    }

    [Fact]
    public void Forward_with_fixed_and_extra_charges()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 2.5m,
            FixedCharge = 5m,
            VatPercent = 15m,
            ExtraCharge = 10m
        };
        var c = Calc.Calculate(2000m, cfg);

        Assert.Equal(55m, c.GatewayCharge);     // 2000 * 2.5% + 5 = 55
        Assert.Equal(8.25m, c.Vat);             // 55 * 15%
        Assert.Equal(10m, c.ExtraCharge);
        Assert.Equal(2073.25m, c.CustomerPays); // 2000 + 55 + 8.25 + 10
        Assert.Equal(2000m, c.MerchantReceives);
    }

    [Fact]
    public void Backward_with_extra_charge_deducts_extra_from_merchant_payout()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 2.5m,
            VatPercent = 15m,
            ExtraCharge = 10m
        };
        var c = Calc.Calculate(2000m, cfg);

        Assert.Equal(2000m, c.CustomerPays);
        // 2000 - 50 (charge) - 7.50 (vat) - 10 (extra) = 1932.50
        Assert.Equal(1932.50m, c.MerchantReceives);
    }

    [Fact]
    public void Zero_order_amount_yields_zero_everywhere()
    {
        var cfg = new PaymentChargeConfig { ChargeBearer = ChargeBearer.Forward, ChargePercent = 1.85m, VatPercent = 15m };
        var c = Calc.Calculate(0m, cfg);
        Assert.Equal(0m, c.GatewayCharge);
        Assert.Equal(0m, c.Vat);
        Assert.Equal(0m, c.CustomerPays);
        Assert.Equal(0m, c.MerchantReceives);
    }

    [Fact]
    public void Banker_rounding_keeps_half_to_even()
    {
        // Construct an amount where .005 falls on an odd unit so banker's
        // rounding rounds DOWN to 0.00 instead of UP — proves we're not
        // using AwayFromZero.
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 0.5m,   // 1.00 * 0.5% = 0.005 → rounds to 0.00 (even) under banker's
            VatPercent = 0m
        };
        var c = Calc.Calculate(1.00m, cfg);
        Assert.Equal(0.00m, c.GatewayCharge);
    }

    [Fact]
    public void Negative_order_amount_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Calc.Calculate(-1m, new PaymentChargeConfig()));
    }

    [Fact]
    public void Null_config_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Calc.Calculate(100m, null!));
    }
}
