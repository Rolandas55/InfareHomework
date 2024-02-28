namespace InfareHomework.Models;

public class SearchParam
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public DateTime DateDeparture { get; set; }
    public DateTime DateArrival { get; set; }
    public string Filter { get; set; } = "";
}