using gestao_chamados.Data;

namespace gestao_chamados.Tests;

public class BusinessCalendarTests
{
    // 2026-10-15 é quinta-feira; 16 sexta; 17 sábado; 18 domingo; 19 segunda.
    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static BusinessCalendar Calendar(string zone = "UTC", params string[] holidays) =>
        new(new BusinessHoursOptions
        {
            TimeZone = zone,
            Start = "08:00",
            End = "18:00",
            Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            Holidays = [.. holidays]
        });

    [Fact]
    public void AddBusinessTime_StaysInsideTheSameDay()
    {
        Assert.Equal(Utc(15, 12), Calendar().AddBusinessTime(Utc(15, 10), TimeSpan.FromHours(2)));
    }

    [Fact]
    public void AddBusinessTime_RollsOverToTheNextBusinessDay()
    {
        // Quinta 17h + 3h = 1h na quinta + 2h na sexta (a partir das 8h).
        Assert.Equal(Utc(16, 10), Calendar().AddBusinessTime(Utc(15, 17), TimeSpan.FromHours(3)));
    }

    [Fact]
    public void AddBusinessTime_SkipsTheWeekend()
    {
        Assert.Equal(Utc(19, 10), Calendar().AddBusinessTime(Utc(16, 17), TimeSpan.FromHours(3)));
    }

    [Theory]
    [InlineData(17, 12, 19, 10)] // sábado ao meio-dia → segunda 8h + 2h
    [InlineData(15, 6, 15, 9)]   // antes do expediente → espera abrir
    [InlineData(15, 19, 16, 9)]  // depois do expediente → próximo dia
    public void AddBusinessTime_StartingOutsideBusinessHoursWaitsForTheNextWindow(int day, int hour, int expectedDay, int expectedHour)
    {
        var due = Calendar().AddBusinessTime(Utc(day, hour), TimeSpan.FromHours(day == 17 ? 2 : 1));

        Assert.Equal(Utc(expectedDay, expectedHour), due);
    }

    [Fact]
    public void AddBusinessTime_SkipsHolidays()
    {
        // Sexta 16/10 é feriado: quinta 17h + 3h cai na segunda.
        Assert.Equal(Utc(19, 10), Calendar("UTC", "2026-10-16").AddBusinessTime(Utc(15, 17), TimeSpan.FromHours(3)));
    }

    [Fact]
    public void AddBusinessTime_ZeroDurationReturnsTheStart()
    {
        Assert.Equal(Utc(17, 12), Calendar().AddBusinessTime(Utc(17, 12), TimeSpan.Zero));
    }

    [Fact]
    public void AddBusinessTime_ExactlyFillingTheDayEndsAtClosingTime()
    {
        Assert.Equal(Utc(15, 18), Calendar().AddBusinessTime(Utc(15, 8), TimeSpan.FromHours(10)));
    }

    [Fact]
    public void BusinessTimeBetween_CountsOnlyBusinessHours()
    {
        var calendar = Calendar();

        // Quinta 10h → sexta 10h = 8h na quinta + 2h na sexta.
        Assert.Equal(TimeSpan.FromHours(10), calendar.BusinessTimeBetween(Utc(15, 10), Utc(16, 10)));

        // Sexta 17h → segunda 9h = 1h + 1h.
        Assert.Equal(TimeSpan.FromHours(2), calendar.BusinessTimeBetween(Utc(16, 17), Utc(19, 9)));
    }

    [Fact]
    public void BusinessTimeBetween_IsZeroOutsideBusinessHoursAndWhenReversed()
    {
        var calendar = Calendar();

        Assert.Equal(TimeSpan.Zero, calendar.BusinessTimeBetween(Utc(17, 9), Utc(18, 17)));
        Assert.Equal(TimeSpan.Zero, calendar.BusinessTimeBetween(Utc(16, 10), Utc(15, 10)));
    }

    [Fact]
    public void AddAndBetween_AreInverseOfEachOther()
    {
        var calendar = Calendar("UTC", "2026-10-19");
        var start = Utc(14, 13, 30);

        foreach (var hours in new[] { 1, 7, 12, 30, 55 })
        {
            var end = calendar.AddBusinessTime(start, TimeSpan.FromHours(hours));
            Assert.Equal(TimeSpan.FromHours(hours), calendar.BusinessTimeBetween(start, end));
        }
    }

    [Fact]
    public void AddBusinessTime_UsesTheConfiguredTimeZone()
    {
        // Em São Paulo (UTC-3, sem horário de verão desde 2019) o expediente 8h–18h é 11h–21h UTC.
        var calendar = Calendar("America/Sao_Paulo");

        // 14h UTC = 11h locais; +1h = 15h UTC.
        Assert.Equal(Utc(15, 15), calendar.AddBusinessTime(Utc(15, 14), TimeSpan.FromHours(1)));

        // 20h30 UTC = 17h30 locais; 30 min até fechar + 30 min no dia seguinte (8h30 locais = 11h30 UTC).
        Assert.Equal(Utc(16, 11, 30), calendar.AddBusinessTime(Utc(15, 20, 30), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Constructor_RejectsInvalidConfiguration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new BusinessCalendar(new BusinessHoursOptions { Start = "18:00", End = "08:00" }));
        Assert.Throws<InvalidOperationException>(() =>
            new BusinessCalendar(new BusinessHoursOptions { Start = "abc" }));
        Assert.Throws<InvalidOperationException>(() =>
            new BusinessCalendar(new BusinessHoursOptions { Holidays = ["25/12/2026"] }));
    }

    [Fact]
    public void Constructor_DefaultsToMondayThroughFridayWhenNoDaysAreConfigured()
    {
        var calendar = new BusinessCalendar(new BusinessHoursOptions { TimeZone = "UTC" });

        Assert.Equal(5, calendar.Days.Count);
        Assert.DoesNotContain(DayOfWeek.Saturday, calendar.Days);
    }
}
