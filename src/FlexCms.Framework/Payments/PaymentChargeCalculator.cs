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
/// send to the upstream gateway (Forward → customer is charged the larger
/// total; Backward → customer is charged the original order amount).
///
/// <para>
/// All math is in <see cref="decimal"/> so monetary precision is preserved.
/// Results are rounded to 2 decimal places using banker's rounding (the
/// same convention BD payment gateways use on their server side).
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

        var gatewayCharge = Round(orderAmount * config.ChargePercent / 100m + config.FixedCharge);
        var vat = Round(gatewayCharge * config.VatPercent / 100m);
        var extra = Round(config.ExtraCharge);

        decimal customerPays;
        decimal merchantReceives;

        if (config.ChargeBearer == ChargeBearer.Forward)
        {
            // Customer eats the cost — they pay order + charge + vat + extra.
            // Merchant receives the original order amount in full.
            customerPays = Round(orderAmount + gatewayCharge + vat + extra);
            merchantReceives = orderAmount;
        }
        else
        {
            // Merchant eats the cost — customer pays only the order amount.
            // Gateway deducts charge + vat + extra from the merchant's payout.
            customerPays = orderAmount;
            merchantReceives = Round(orderAmount - gatewayCharge - vat - extra);
        }

        return new PaymentCharge(
            OrderAmount: orderAmount,
            GatewayCharge: gatewayCharge,
            Vat: vat,
            ExtraCharge: extra,
            CustomerPays: customerPays,
            MerchantReceives: merchantReceives,
            Bearer: config.ChargeBearer);
    }

    /// <summary>Round to 2 dp, banker's rounding to match gateway server behaviour.</summary>
    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);
}
