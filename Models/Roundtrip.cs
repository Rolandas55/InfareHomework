namespace InfareHomework.Models;

public class Roundtrip(Availability availability, Journey oJourney, Journey iJourney)
{
    public Availability Availability = availability;
    public Journey OutJourney = oJourney;
    public Journey InJourney = iJourney;
}