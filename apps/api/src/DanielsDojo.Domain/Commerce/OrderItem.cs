using DanielsDojo.Domain.Catalog;

namespace DanielsDojo.Domain.Commerce;

/// <summary>
/// One purchased line. Name and amount are snapshotted at purchase time so later catalog
/// or price edits never rewrite history. <see cref="CourseId"/> is stored explicitly so the
/// purchased scope survives changes to the offer.
/// </summary>
public sealed class OrderItem
{
    /// <summary>Application-owned primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning order.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Purchased offer. Unique within the order.</summary>
    public Guid OfferId { get; set; }

    /// <summary>Price row charged.</summary>
    public Guid PriceId { get; set; }

    /// <summary>
    /// Course granted by this line, when it grants one. A lifetime purchase names its course;
    /// a membership line grants whatever the membership covers and names none.
    /// </summary>
    public Guid? CourseId { get; set; }

    /// <summary>Offer name captured at purchase time.</summary>
    public string OfferName { get; set; } = string.Empty;

    /// <summary>Unit amount in minor units, captured at purchase time.</summary>
    public long UnitAmountMinor { get; set; }

    /// <summary>Uppercase ISO-4217 currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Quantity purchased. Constrained to 1 at launch.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>Line total in minor units. Equals unit amount times quantity.</summary>
    public long LineTotalMinor { get; set; }

    /// <summary>The owning order.</summary>
    public Order? Order { get; set; }

    /// <summary>The purchased offer.</summary>
    public Offer? Offer { get; set; }

    /// <summary>The price charged.</summary>
    public Price? Price { get; set; }

    /// <summary>The course granted.</summary>
    public Course? Course { get; set; }
}
