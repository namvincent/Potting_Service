public class Potting{
    public string? Barcode { get; set; }
    public string? Status { get; set; }
    public string? Machine { get; set; }
    public DateTime PottingTime { get; set; } = DateTime.Now;
    public PottingResult? PottingResult { get; set; }
}
public class PottingResult{
    public string? Station { get; set; }
    public string? LowerLimit { get; set; } = "0";
    public string? Weight { get; set; } = "0";
    public string? UpperLimit { get; set; } = "0";
}