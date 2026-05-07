using FlexCms.Framework.Payments;
using Xunit;

namespace FlexCms.Tests.Unit.Phase12;

/// <summary>
/// Locks down Forward / Backward charge math against NetCoreCMS's bKash
/// helper. Each scenario corresponds to a real shape we expect to see in
/// production.
/// </summary>
public class PaymentChargeCalculatorTests
{
    private static readonly PaymentChargeCalculator Calc = new();

    // ── Forward ──────────────────────────────────────────────────────────────

    [Fact]
    public void Forward_straight_percent_on_order_amount()
    {
        // bKash standard: 1.85%, no VAT/fixed/extra. NetCoreCMS:
        // GetStraightServiceFee(1000, 1.85) = 18.50
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 1.85m
        };
        var c = Calc.Calculate(1000m, cfg);
        Assert.Equal(18.50m, c.GatewayCharge);
        Assert.Equal(1018.50m, c.CustomerPays);
        Assert.Equal(1000m, c.MerchantReceives);
    }

    [Fact]
    public void Forward_with_fixed_and_vat()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 1.85m,
            FixedCharge = 2m,
            VatPercent = 15m
        };
        var c = Calc.Calculate(1000m, cfg);
        Assert.Equal(20.50m, c.GatewayCharge);  // 1000 * 1.85% + 2 = 20.50
        Assert.Equal(3.08m, c.Vat);             // 20.50 * 15% = 3.075 → ceiling-on-3rd-digit → 3.08
        Assert.Equal(1023.58m, c.CustomerPays);
        Assert.Equal(1000m, c.MerchantReceives);
    }

    [Fact]
    public void Forward_extra_charge_is_pass_through_revenue_for_merchant()
    {
        // Product 200, 5% forward, 10 service charge.
        // Customer: 200 + 10 (charge) + 10 (service) = 220
        // Merchant: 200 (order) + 10 (service) = 210
        // No charge calculated on the 10 service (default ApplyChargeOnExtra=false).
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 5m,
            ExtraCharge = 10m
        };
        var c = Calc.Calculate(200m, cfg);
        Assert.Equal(10m, c.GatewayCharge);
        Assert.Equal(10m, c.ExtraCharge);
        Assert.Equal(220m, c.CustomerPays);
        Assert.Equal(210m, c.MerchantReceives);
    }

    [Fact]
    public void Forward_with_ApplyChargeOnExtra_includes_extra_in_charge_base()
    {
        // Same as above but ApplyChargeOnExtra=true: charge calc on 200+10=210.
        // Customer: 200 + 10.50 (5% of 210) + 10 = 220.50
        // Merchant: 210
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 5m,
            ExtraCharge = 10m,
            ApplyChargeOnExtra = true
        };
        var c = Calc.Calculate(200m, cfg);
        Assert.Equal(10.50m, c.GatewayCharge);
        Assert.Equal(220.50m, c.CustomerPays);
        Assert.Equal(210m, c.MerchantReceives);
    }

    // ── Backward (NetCoreCMS GetBackCalculationServiceFee) ───────────────────

    [Fact]
    public void Backward_back_calculates_fee_so_merchant_nets_full_order()
    {
        // NetCoreCMS: GetBackCalculationServiceFee(1000, 1.85)
        //   = 1000 / (1 - 0.0185) - 1000
        //   = 1000 / 0.9815 - 1000
        //   = 1018.8487... - 1000
        //   = 18.8487... → ceiling-on-3rd-digit → 18.85
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 1.85m
        };
        var c = Calc.Calculate(1000m, cfg);
        Assert.Equal(18.85m, c.GatewayCharge);
        Assert.Equal(1018.85m, c.CustomerPays);
        Assert.Equal(1000m, c.MerchantReceives);
    }

    [Fact]
    public void Backward_with_extra_charge_per_user_spec()
    {
        // User's example: product 200, 5% backward, 10 service charge.
        // Backward fee = 200 / (1 - 0.05) - 200 = 200 / 0.95 - 200
        //              = 210.5263... - 200 = 10.5263... → 10.53
        // Customer: 200 + 10.53 + 10 = 220.53
        // Merchant: 200 + 10 = 210 (back-calc means merchant nets full order)
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 5m,
            ExtraCharge = 10m
        };
        var c = Calc.Calculate(200m, cfg);
        Assert.Equal(10.53m, c.GatewayCharge);
        Assert.Equal(220.53m, c.CustomerPays);
        Assert.Equal(210m, c.MerchantReceives);
    }

    [Fact]
    public void Backward_pathological_100_percent_rate_yields_zero_charge_no_div_by_zero()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 100m
        };
        var c = Calc.Calculate(1000m, cfg);
        Assert.Equal(0m, c.GatewayCharge);
        Assert.Equal(1000m, c.CustomerPays);
    }

    // ── Rounding (NetCoreCMS FormatTwoDecimalPointAmount) ────────────────────

    [Fact]
    public void Rounds_up_when_third_decimal_is_non_zero()
    {
        // 18.8487 → 18.85 (ceiling, not banker's)
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Backward,
            ChargePercent = 1.85m
        };
        var c = Calc.Calculate(1000m, cfg);
        Assert.Equal(18.85m, c.GatewayCharge);
    }

    [Fact]
    public void Uses_bankers_rounding_when_third_decimal_is_zero()
    {
        // 1.005 → 1.00 (banker's, half-to-even). With third digit = 5,
        // (1.005 * 1000) % 10 == 5 which is non-zero, so this hits the
        // ceiling branch instead → 1.01. So construct an exact .00 case:
        // 1.000 → third digit zero → banker's rounds to 1.00.
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 0m,
            FixedCharge = 1.000m
        };
        var c = Calc.Calculate(0m, cfg);
        Assert.Equal(1.00m, c.GatewayCharge);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void Zero_order_amount_yields_zero_charge_and_vat()
    {
        var cfg = new PaymentChargeConfig
        {
            ChargeBearer = ChargeBearer.Forward,
            ChargePercent = 1.85m,
            VatPercent = 15m
        };
        var c = Calc.Calculate(0m, cfg);
        Assert.Equal(0m, c.GatewayCharge);
        Assert.Equal(0m, c.Vat);
        Assert.Equal(0m, c.CustomerPays);
        Assert.Equal(0m, c.MerchantReceives);
    }

    [Fact]
    public void Zero_order_with_extra_only_passes_through()
    {
        // Edge: subscription's free trial with a 5tk processing fee.
        var cfg = new PaymentChargeConfig { ExtraCharge = 5m };
        var c = Calc.Calculate(0m, cfg);
        Assert.Equal(0m, c.GatewayCharge);
        Assert.Equal(5m, c.CustomerPays);
        Assert.Equal(5m, c.MerchantReceives);
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
