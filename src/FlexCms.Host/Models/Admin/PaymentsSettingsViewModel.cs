using System.ComponentModel.DataAnnotations;
using FlexCms.Framework.Payments;

namespace FlexCms.Host.Models.Admin;

/// <summary>
/// Composite view-model for the payments admin page. Each gateway gets its
/// own section because credential shapes differ; the "leave secret blank to
/// keep" pattern is shared with the SMTP/SMS settings page.
/// </summary>
public class PaymentsSettingsViewModel
{
    // ── General ──────────────────────────────────────────────────────────────
    [Display(Name = "Enable payments")]
    public bool Enabled { get; set; }

    [Display(Name = "Active gateway")]
    public string ActiveGateway { get; set; } = PaymentGateways.Bkash;

    [Display(Name = "Currency (ISO 4217)")]
    public string Currency { get; set; } = "BDT";

    // ── Charge-config sub-VM (used by all 3 gateway sections) ────────────────
    public class ChargeVm
    {
        [Display(Name = "Who pays the charge")]
        public ChargeBearer Bearer { get; set; } = ChargeBearer.Forward;

        [Display(Name = "Charge %")]
        [Range(0, 100)]
        public decimal ChargePercent { get; set; }

        [Display(Name = "Fixed fee")]
        [Range(0, double.MaxValue)]
        public decimal FixedCharge { get; set; }

        [Display(Name = "VAT % (on charge)")]
        [Range(0, 100)]
        public decimal VatPercent { get; set; }

        [Display(Name = "Extra (service) charge")]
        [Range(0, double.MaxValue)]
        public decimal ExtraCharge { get; set; }

        [Display(Name = "Apply gateway charge on extra")]
        public bool ApplyChargeOnExtra { get; set; }

        public PaymentChargeConfig ToConfig() => new()
        {
            ChargeBearer = Bearer,
            ChargePercent = ChargePercent,
            FixedCharge = FixedCharge,
            VatPercent = VatPercent,
            ExtraCharge = ExtraCharge,
            ApplyChargeOnExtra = ApplyChargeOnExtra
        };

        public static ChargeVm From(PaymentChargeConfig c) => new()
        {
            Bearer = c.ChargeBearer,
            ChargePercent = c.ChargePercent,
            FixedCharge = c.FixedCharge,
            VatPercent = c.VatPercent,
            ExtraCharge = c.ExtraCharge,
            ApplyChargeOnExtra = c.ApplyChargeOnExtra
        };
    }

    // ── bKash ────────────────────────────────────────────────────────────────
    public BkashVm Bkash { get; set; } = new();

    public class BkashVm
    {
        [Display(Name = "Enable bKash")]
        public bool Enabled { get; set; }
        [Display(Name = "Test (sandbox) mode")]
        public bool TestMode { get; set; } = true;

        [Display(Name = "App Key (X-APP-Key)")]
        public string AppKey { get; set; } = "";

        [Display(Name = "App Secret (leave blank to keep)")]
        [DataType(DataType.Password)]
        public string? AppSecret { get; set; }
        public bool HasAppSecret { get; set; }

        [Display(Name = "Username")]
        public string Username { get; set; } = "";

        [Display(Name = "Password (leave blank to keep)")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
        public bool HasPassword { get; set; }

        [Display(Name = "Merchant number")]
        public string MerchantNumber { get; set; } = "";

        [Display(Name = "Endpoint override (advanced)")]
        public string EndpointOverride { get; set; } = "";

        public ChargeVm Charge { get; set; } = new();
    }

    // ── SSLCommerz ───────────────────────────────────────────────────────────
    public SslcommerzVm Sslcommerz { get; set; } = new();

    public class SslcommerzVm
    {
        [Display(Name = "Enable SSLCommerz")]
        public bool Enabled { get; set; }
        [Display(Name = "Test (sandbox) mode")]
        public bool TestMode { get; set; } = true;

        [Display(Name = "Store ID")]
        public string StoreId { get; set; } = "";

        [Display(Name = "Store password (leave blank to keep)")]
        [DataType(DataType.Password)]
        public string? StorePassword { get; set; }
        public bool HasStorePassword { get; set; }

        [Display(Name = "Endpoint override (advanced)")]
        public string EndpointOverride { get; set; } = "";

        public ChargeVm Charge { get; set; } = new();
    }

    // ── Nagad ────────────────────────────────────────────────────────────────
    public NagadVm Nagad { get; set; } = new();

    public class NagadVm
    {
        [Display(Name = "Enable Nagad")]
        public bool Enabled { get; set; }
        [Display(Name = "Test (sandbox) mode")]
        public bool TestMode { get; set; } = true;

        [Display(Name = "Merchant ID")]
        public string MerchantId { get; set; } = "";

        [Display(Name = "Merchant number")]
        public string MerchantNumber { get; set; } = "";

        [Display(Name = "Merchant public key (PEM)")]
        public string MerchantPublicKey { get; set; } = "";

        [Display(Name = "Merchant private key PEM (leave blank to keep)")]
        public string? MerchantPrivateKey { get; set; }
        public bool HasMerchantPrivateKey { get; set; }

        [Display(Name = "Nagad public key (PEM)")]
        public string NagadPublicKey { get; set; } = "";

        [Display(Name = "Endpoint override (advanced)")]
        public string EndpointOverride { get; set; } = "";

        public ChargeVm Charge { get; set; } = new();
    }
}
