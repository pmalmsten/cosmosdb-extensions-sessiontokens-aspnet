
using Flurl.Http;
using Newtonsoft.Json;

var apiBaseUrl = "http://localhost/Counter";
var cookieJar = new CookieJar();

for (int i = 0; i < 5; i++)
{
    var counterValueBeforeWrite = await apiBaseUrl
        .WithCookies(cookieJar)
        .GetJsonAsync<Counter>();
    Console.WriteLine($"Counter value before write: {counterValueBeforeWrite.Count}");

    var incrementedCounterValue = await apiBaseUrl
        .WithCookies(cookieJar)
        .PostAsync()
        .ReceiveJson<Counter>();

    Console.WriteLine($"Counter incremented to: {incrementedCounterValue.Count}");

    for (int j = 0; j < 15; j++)
    {
        var request = apiBaseUrl.WithCookies(cookieJar);
        var response = await request.GetAsync();
        
        var currentCounterValue = await response.GetJsonAsync<Counter>();

        var anyCookiesSent = request.Cookies.Any();
        var anyCookiesReceived = response.Cookies.Any();
        var readWasConsistent = currentCounterValue.Count == incrementedCounterValue.Count;

        PrintWhetherReadWasConsistentToConsole(readWasConsistent, currentCounterValue, anyCookiesSent, anyCookiesReceived);
    }
}

void PrintWhetherReadWasConsistentToConsole(bool readWasConsistent, Counter counter, bool anyCookiesSent, bool anyCookiesReceived)
{
    ChangeConsoleColorForInconsistentResponses(readWasConsistent);
    Console.Write($"Read counter value: {counter.Count}");
    if (!readWasConsistent)
    {
        Console.Write(" (inconsistent)");
    }
    
    if (anyCookiesSent)
    {
        Console.Write(" (cookies sent)");
    }
    
    if (anyCookiesReceived)
    {
        Console.Write(" (cookies received)");
    }

    Console.ResetColor();
    Console.WriteLine();
}

void ChangeConsoleColorForInconsistentResponses(bool readWasConsistent)
{
    Console.ForegroundColor = ConsoleColor.White;
    if (readWasConsistent)
    {
        Console.BackgroundColor = ConsoleColor.DarkGreen;
    }
    else
    {
        Console.BackgroundColor = ConsoleColor.DarkYellow;
        Console.ForegroundColor = ConsoleColor.Black;
    }
}

record Counter(
    [property:JsonProperty("id")] string Id, 
    [property:JsonProperty("count")] int Count);