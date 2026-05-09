namespace FlexCms.Core.Models.Settings;

public class ThemeSettings
{
    public const string Key = "site:theme";

    // ── Light mode ─────────────────────────────────────────────────────────
    public string Primary { get; set; } = "#0d6efd";
    public string Secondary { get; set; } = "#6c757d";
    public string Success { get; set; } = "#198754";
    public string Danger { get; set; } = "#dc3545";
    public string Warning { get; set; } = "#ffc107";
    public string Info { get; set; } = "#0dcaf0";
    public string BodyBg { get; set; } = "#ffffff";
    public string BodyColor { get; set; } = "#212529";
    public string BorderColor { get; set; } = "#dee2e6";
    public string LinkColor { get; set; } = "#0d6efd";
    public string CardBg { get; set; } = "#ffffff";
    public string NavbarBg { get; set; } = "#ffffff";
    public string FooterBg { get; set; } = "#f8f9fa";
    public string FooterColor { get; set; } = "#6c757d";

    // ── Dark mode ──────────────────────────────────────────────────────────
    public string DarkPrimary { get; set; } = "#6ea8fe";
    public string DarkSecondary { get; set; } = "#a7acb1";
    public string DarkSuccess { get; set; } = "#75b798";
    public string DarkDanger { get; set; } = "#ea868f";
    public string DarkWarning { get; set; } = "#ffda6a";
    public string DarkInfo { get; set; } = "#6edff6";
    public string DarkBodyBg { get; set; } = "#1e2a3a";
    public string DarkBodyColor { get; set; } = "#dee2e6";
    public string DarkBorderColor { get; set; } = "#2d3d50";
    public string DarkLinkColor { get; set; } = "#6ea8fe";
    public string DarkCardBg { get; set; } = "#243447";
    public string DarkNavbarBg { get; set; } = "#1e2a3a";
    public string DarkFooterBg { get; set; } = "#16202d";
    public string DarkFooterColor { get; set; } = "#a7acb1";

    // ── Admin sidebar (shared light/dark) ──────────────────────────────────
    public string SidebarBg { get; set; } = "#1e2a3a";
    public string SidebarColor { get; set; } = "#cdd6e0";
    public string DarkSidebarBg { get; set; } = "#111827";
    public string DarkSidebarColor { get; set; } = "#9ca3af";
}
