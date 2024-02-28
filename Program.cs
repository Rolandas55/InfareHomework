using System.Text;
using InfareHomework.Models;
using Newtonsoft.Json.Linq;
using CsvHelper;
using System.Globalization;

namespace InfareHomework;

class Program
{

    static class Constants
    {
    public const int MaxFlightsNumber = 2;
    public const string Separator = ",";
    }

    static void Main(string[] args)
    {
        //Create addresses and StringBuilders for result files
        string allTrips = Path.Combine(Environment.CurrentDirectory, @"..\..\..\Data\AllTrips.csv");
        string cheapestTrips = Path.Combine(Environment.CurrentDirectory, @"..\..\..\Data\CheapestTrips.csv");
        StringBuilder roundtripsOutput = CreateOutput();
        StringBuilder cheapestOutput = CreateOutput();

        //Adding address of search parameter sets file which I made
        StreamReader reader = new(@"..\..\..\Data\Searches.csv");

        CsvReader csvData = new(reader, CultureInfo.InvariantCulture);
        IEnumerable<SearchParam> searchParams = csvData.GetRecords<SearchParam>();

        //Looping every search parameter set from CSV file
        foreach (SearchParam search in searchParams)
        {
            //Creating url and calling it to get a response
            string dDeparture = search.DateDeparture.ToString("yyyy-MM-dd");
            string dArrival = search.DateArrival.ToString("yyyy-MM-dd");
            string url = $"http://homeworktask.infare.lt/search.php?from={search.From}&to={search.To}&depart={dDeparture}&return={dArrival}";
            var response = CallUrl(url).Result;
            //trying to convert url response to json object
            JObject flightData;
            try
            {
                flightData = JObject.Parse(response);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}. Message: {response} with from: {search.From} to: {search.To}");
                continue;
            }
            //if conversion is successful, saving data into two Lists of classes: Availability and Journey
            IList<Availability> availabilities = DeserializeAvailabilities(flightData);
            IList<Journey> flights = DeserializeJourneys(flightData);
            //Connecting availabilities to all combinations of outbound and inbound journeys by recommendationId
            //and saving to Roundtrip class
            IList<Roundtrip> roundtripFlights = CombineRoundtrips(flights, availabilities, search.Filter);
            //Finding cheapest Roundtrips
            IList<Roundtrip> cheapestFlights = FindCheapestFlights(roundtripFlights);
            //Saving all flights and cheapest flights data to StringBuilder
            SaveToOutput(roundtripFlights, roundtripsOutput);
            SaveToOutput(cheapestFlights, cheapestOutput);

        }
        //Writing StringBuilders data to CSV files
        WriteToCsv(allTrips, roundtripsOutput);
        WriteToCsv(cheapestTrips, cheapestOutput);
    }

    private static async Task<string> CallUrl(string url)
    {
        HttpClient httpClient = new();
        var response = await httpClient.GetStringAsync(url);
        return response;
    }

    private static IList<Availability> DeserializeAvailabilities(JObject Data)
    {
        IList<JToken> jTokens = Data["body"]["data"]["totalAvailabilities"].Children().ToList();
        List<Availability> availabilities = [];
        foreach (JToken token in jTokens)
        {
            Availability availability = token.ToObject<Availability>();
            availabilities.Add(availability);
        }
        return availabilities;
    }

    private static IList<Journey> DeserializeJourneys(JObject Data)
    {
        IList<JToken> jTokens = Data["body"]["data"]["journeys"].Children().ToList();
        List<Journey> journeys = [];
        foreach (JToken token in jTokens)
        {
            Journey journey = token.ToObject<Journey>();
            for ( int i = 0; i < journey.Flights.Length; i++)
            {
                journey.Flights[i].DepartureCode = (string)token["flights"][i]["airportDeparture"]["code"];
                journey.Flights[i].ArrivalCode = (string)token["flights"][i]["airportArrival"]["code"];
            }
            journeys.Add(journey);
        }
        return journeys;
    }

    private static List<Roundtrip> CombineRoundtrips(IList<Journey> flights, IList<Availability> availabilities, string filter)
    {
        List<Roundtrip> roundtripFlights = [];
        var groups = 
            from f in flights
            group f by f.RecommendationId;

        foreach (var group in groups)
        {
            List<Journey> outFlights = [];
            List<Journey> inFlights = [];
            foreach (var g in group)
            {
                //case 1 take all direct flights
                //case 2 if no filter takes all else checks if connection flight same as filter
                //default skips all flights
                switch (g.Flights.Length)
                {
                    case 1:        
                        break;
                    case 2:
                        if (g.Flights[1].DepartureCode != filter && !string.IsNullOrEmpty(filter))
                        {
                            continue;
                        }
                        break;
                    default:
                        continue;
                }

                if (g.Direction == "I")
                {
                    outFlights.Add(g);
                }
                else if (g.Direction == "V")
                {
                    inFlights.Add(g);
                }
            }
            foreach (Journey outFlight in outFlights)
            {
                foreach (Journey inFlight in inFlights)
                {
                    Availability groupAvailability = availabilities.FirstOrDefault(x => x.RecommendationId == outFlight.RecommendationId);
                    if (groupAvailability == null)
                    {
                        Console.WriteLine("Failed to find group id");
                        continue;
                    }
                    Roundtrip newRoundtrip = new(groupAvailability, outFlight, inFlight);
                    roundtripFlights.Add(newRoundtrip);
                }
            }
        }

        return roundtripFlights;
    }

    private static IList<Roundtrip> FindCheapestFlights(IList<Roundtrip> Allroundtrips)
    {
        IList<Roundtrip> cheapestFlights = [];
        var flightsGroups = Allroundtrips
            .GroupBy(rf => new
            {
                rf1 = rf.OutJourney.Flights[0].DepartureCode,
                rf2 = rf.InJourney.Flights[0].DepartureCode
            });
        foreach (var group in flightsGroups) {
            Roundtrip cheapest = group.First();
            double cheapestTaxes = cheapest.OutJourney.ImportTaxAdl + cheapest.InJourney.ImportTaxAdl;
            foreach (Roundtrip g in group)
            {
                double taxes = g.OutJourney.ImportTaxAdl + g.InJourney.ImportTaxAdl;
                if (g.Availability.Total + taxes < cheapest.Availability.Total + cheapestTaxes)
                {
                    cheapest = g;
                    cheapestTaxes = taxes;
                }
            }
            cheapestFlights.Add(cheapest);
        }
        return cheapestFlights;
    }

    private static StringBuilder CreateOutput()
    {
        StringBuilder output = new();
        string[] headings = [ "Price", "Taxes", "outbound 1 airport departure", "outbound 1 airport arrival", "outbound 1 time departure",
            "outbound 1 time arrival", "outbound 1 flight number", "outbound 2 airport departure", "outbound 2 airport arrival",
            "outbound 2 time departure", "outbound 2 time arrival", "outbound 2 flight number", "inbound 1 airport departure", "inbound 1 airport arrival",
            "inbound 1 time departure", "inbound 1 time arrival", "inbound 1 flight number", "inbound 2 airport departure", "inbound 2 airport arrival",
            "inbound 2 time departure", "inbound 2 time arrival", "inbound 2 flight number"];
        output.AppendLine(string.Join(Constants.Separator, headings));
        return output;
    }

    private static void SaveToOutput(IList<Roundtrip> roundtripFlights, StringBuilder output)
    {
        foreach (Roundtrip trip in roundtripFlights)
        {

            List<string> newLine = [];
            newLine.AddRange([trip.Availability.Total.ToString(), (trip.OutJourney.ImportTaxAdl + trip.InJourney.ImportTaxAdl).ToString()]);
            for (int i = 0; i < Constants.MaxFlightsNumber; i++)
            {
                if (i+1 > trip.OutJourney.Flights.Length)
                {
                    newLine.AddRange(["", "", "", "", ""]);
                }
                else 
                {
                newLine.AddRange([
                    trip.OutJourney.Flights[i].DepartureCode, trip.OutJourney.Flights[i].ArrivalCode, trip.OutJourney.Flights[i].DateDeparture,
                    trip.OutJourney.Flights[i].DateArrival, trip.OutJourney.Flights[i].CompanyCode + trip.OutJourney.Flights[i].Number
                ]);
                }
            }
            for (int i = 0; i < trip.InJourney.Flights.Count(); i++)
            {
                if (i < trip.InJourney.Flights.Count())
                {
                newLine.AddRange([
                    trip.InJourney.Flights[i].DepartureCode, trip.InJourney.Flights[i].ArrivalCode, trip.InJourney.Flights[i].DateDeparture,
                    trip.InJourney.Flights[i].DateArrival, trip.InJourney.Flights[i].CompanyCode + trip.InJourney.Flights[i].Number
                ]);
                }
            }
            output.AppendLine(string.Join(Constants.Separator, newLine));
        }
    }

    private static void WriteToCsv(string file, StringBuilder data)
    {
        try
        {
            File.WriteAllText(file, data.ToString());
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to write data to csv with error {e.Message}");
        }
    }
}
