namespace FlexCms.Framework.Payments;

/// <summary>
/// Result of applying a <see cref="PaymentChargeConfig"/> to an order amount.
/// </summary>
public sealed record PaymentCharge(
    decimal OrderAmount,
    decimal GatewayCharge,
    decimal Vat,
    decimal ExtraCharge,
    decimal CustomerPays,
    decimal MerchantReceives,
    ChargeBearer Bearer);

/// <summary>
/// Computes <see cref="PaymentCharge"/> from an order amount + a charge
/// configuration. Used by gateway impls to figure out the actual amount to
/// send to the upstream gateway.
///
/// <para>
/// Math mirrors NetCoreCMS's bKash module:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Forward</b>: <c>fee = base × rate / 100 + fixed</c>. Customer pays
///     order + fee + VAT + extra. Merchant nets order + extra (gateway fee
///     comes off the customer-side total).
///   </item>
///   <item>
///     <b>Backward</b>: <c>fee = base / (1 - rate/100) - base + fixed</c> —
///     back-calculated so the gateway's commission on (base + fee) leaves
///     the merchant with the full base. Customer still pays everything
///     (base + fee + VAT + extra); the term "backward" is about how the fee
///     is *derived*, not about who absorbs it. Merchant nets order + extra.
///   </item>
/// </list>
///
/// <para>
/// <see cref="PaymentChargeConfig.ExtraCharge"/> is the merchant's own service
/// fee — pass-through revenue. It's ALWAYS added to both what the customer
/// pays AND what the merchant receives, regardless of bearer mode.
/// <see cref="PaymentChargeConfig.ApplyChargeOnExtra"/> controls whether the
/// gateway commission is calculated on (order + extra) or just on order.
/// </para>
///
/// <para>
/// All math is in <see cref="decimal"/> for monetary precision. Results are
/// rounded to 2 dp using NetCoreCMS's "ceiling-on-third-digit, banker's
/// otherwise" rule (matches what the BD gateway servers report back).
/// </para>
/// </summary>
public interface IPaymentChargeCalculator
{
    PaymentCharge Calculate(decimal orderAmount, PaymentChargeConfig config);
}

public sealed class PaymentChargeCalculator : IPaymentChargeCalculator
{
    public PaymentCharge Calculate(decimal orderAmount, PaymentChargeConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (orderAmount < 0) throw new ArgumentOutOfRangeException(nameof(orderAmount));

        var extra = Round(config.ExtraCharge);

        // chargeBase: the principal the gateway commission is calculated on.
        // Default: just the order. If ApplyChargeOnExtra is set, the merchant
        // also wants commission applied to their own service fee — matches
        // NetCoreCMS IsServiceChargeApplyOnAdditionalFee.
        var chargeBase = config.ApplyChargeOnExtra ? orderAmount + extra : orderAmount;

        decimal gatewayCharge;
        if (config.ChargeBearer == ChargeBearer.Forward)
        {
            // Forward: straight percent on the base.
            gatewayCharge = Round(chargeBase * config.ChargePercent / 100m + config.FixedCharge);
        }
        else
        {
            // Backward: back-calculated so the gateway takes its cut from the
            // grossed-up amount and leaves the merchant whole.
            // fee = base / (1 - rate/100) - base, guarded against >=100% rates.
            var rate = config.ChargePercent / 100m;
            var grossUp = rate < 1m
                ? chargeBase / (1m - rate) - chargeBase
                : 0m;   // pathological config — refuse to divide by zero
            gatewayCharge = Round(grossUp + config.FixedCharge);
        }

        var vat = Round(gatewayCharge * config.VatPercent / 100m);

        // Customer always pays the full stack. Merchant always receives the
        // order + their service fee — the bearer toggle just changes how the
        // gateway-fee number is derived, not who hands it over.
        var customerPays = Round(orderAmount + gatewayCharge + vat + extra);
        var merchantReceives = Round(orderAmount + extra);

        return new PaymentCharge(
            OrderAmount: orderAmount,
            GatewayCharge: gatewayCharge,
            Vat: vat,
            ExtraCharge: extra,
            CustomerPays: customerPays,
            MerchantReceives: merchantReceives,
            Bearer: config.ChargeBearer);
    }

    /// <summary>
    /// NetCoreCMS-style rounding: if the third decimal digit is non-zero,
    /// round UP (ceiling) to 2 dp; otherwise use banker's rounding. Mirrors
    /// <c>BkashHelper.FormatTwoDecimalPointAmount</c> so totals match what
    /// the gateway server computes on its side.
    /// </summary>
    private static decimal Round(decimal v)
    {
        var thirdDigit = (v * 1000m) % 10m;
        if (thirdDigit > 0m)
            return Math.Ceiling(v * 100m) / 100m;
        return Math.Round(v, 2, MidpointRounding.ToEven);
    }
}
