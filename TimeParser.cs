using System;
using System.Text.RegularExpressions;

namespace AutoShutdown
{
    public class TimeParser
    {
        static bool CheckScheduleEntry(string timeRange)
        {
            var currentTime = DateTime.UtcNow;
            var midnight = DateTime.UtcNow.AddDays(1).Date;
            DateTime? rangeStart = null, rangeEnd = null, parsedDay = null;
            if (string.IsNullOrWhiteSpace(timeRange) ||
                timeRange.Equals("DoNotShutDown", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            try
            {
                // Parse as range if contains '->'
                if (timeRange.Contains("->"))
                {
                    var timeRangeComponents = Regex.Split(timeRange, "->");
                    if (timeRangeComponents.Length == 2)
                    {
                        rangeStart = DateTime.Parse(timeRangeComponents[0]);
                        rangeEnd = DateTime.Parse(timeRangeComponents[1]);

                        // Check for crossing midnight
                        if (rangeStart > rangeEnd)
                        {
                            // If current time is between the start of range and midnight tonight, interpret start time as earlier today and end time as tomorrow
                            if (currentTime > rangeStart && currentTime < midnight)
                            {
                                rangeEnd = rangeEnd.Value.AddDays(1);
                            }
                            // Otherwise interpret start time as yesterday and end time as today
                            else
                            {
                                rangeStart = rangeStart.Value.AddDays(-1);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("WARNING: Invalid time range format. Expects valid .Net DateTime-formatted start time and end time separated by '->'");
                    }
                }
                // Otherwise attempt to parse as a full day entry, e.g. 'Monday' or 'December 25'
                else
                {
                    DayOfWeek dayOfWeek;
                    var isDay = Enum.TryParse(timeRange, out dayOfWeek);
                    if (isDay)
                    {
                        // If specified as day of week, check if today
                        if (dayOfWeek == System.DateTime.UtcNow.DayOfWeek)
                        {
                            parsedDay = DateTime.UtcNow.Date;
                        }
                    }
                    // Otherwise attempt to parse as a date, e.g. 'December 25'
                    else
                    {
                        parsedDay = DateTime.Parse(timeRange);
                    }
                    if (parsedDay != null)
                    {
                        rangeStart = parsedDay; //# Defaults to midnight
                        rangeEnd = parsedDay.Value.AddHours(23).AddMinutes(59).AddSeconds(59); //# End of the same day
                    }
                }
                if (rangeStart.HasValue && rangeEnd.HasValue)
                {
                    // Check if current time falls within range
                    return currentTime > rangeStart.Value && currentTime < rangeEnd.Value;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Record any errors and return false by default
                Console.WriteLine($"WARNING: Exception encountered while parsing time range. Details: {ex.Message}. Check the syntax of entry, e.g. '<StartTime> -> <EndTime>', or days/dates like 'Sunday' and 'December 25'");
                return false;
            }

        }
    }

}