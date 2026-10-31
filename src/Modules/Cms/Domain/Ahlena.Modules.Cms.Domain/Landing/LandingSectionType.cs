namespace Ahlena.Modules.Cms.Domain.Landing;

/// <summary>
/// Closed landing section registry (owner decision 2026-10-12: 12 keys
/// including the virtual stats key). New types need a new owner decision.
/// <c>Stats</c> is virtual: rendered inside hero, served only by
/// GET /api/v1/landing/stats, never stored as a section row.
/// </summary>
public enum LandingSectionType
{
    Hero = 1,
    PartnerLogos = 2,
    About = 3,
    Features = 4,
    ActivityCards = 5,
    Steps = 6,
    DashboardPromo = 7,
    Testimonials = 8,
    Faq = 9,
    ContactForm = 10,
    Footer = 11,
    Stats = 12
}
