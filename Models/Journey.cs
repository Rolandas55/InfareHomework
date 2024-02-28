namespace InfareHomework.Models;

public class Journey
{
    public int RecommendationId { get; set; }
    public string Direction { get; set; } = "";
    public double ImportTaxAdl { get; set; }
    public string CabinClass { get; set; } = "";
    public Flight[] Flights { get; set; } = [];
}