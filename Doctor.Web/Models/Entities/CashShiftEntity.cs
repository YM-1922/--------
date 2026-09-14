using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Doctor.Web.Models.Entities;

public enum ShiftStatus
{
    Open = 1,
    Closed = 2
}

public class CashShift
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    public DateTime? EndTime { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StartingCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ActualCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalCashSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalVisaSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalInstaPaySales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalOtherSales { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ExpectedCash { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Difference { get; set; } // ActualCash - ExpectedCash (+: زيادة, -: عجز)

    public ShiftStatus Status { get; set; } = ShiftStatus.Open;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
