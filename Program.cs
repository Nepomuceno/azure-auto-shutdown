using System;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Resource.Fluent.Authentication;
using Microsoft.Azure.Management.Resource.Fluent.Core;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Linq;
using System.IO;

namespace AutoShutdown
{
    public class ShutdownConfig {
        public bool Simulate { get; set; }
        public string[] Subscriptions { get; set; }
    }
    public class Program
    {
        public static async void AssertVms(string subscriptionId,bool simulate) {
            
            AzureCredentials credentials = AzureCredentials.FromFile("./cred.azure");
            
            var azure = Azure
                    .Configure()
                    .WithLogLevel(HttpLoggingDelegatingHandler.Level.NONE)
                    .Authenticate(credentials)
                    .WithSubscription(subscriptionId);

            var azureVms = azure.VirtualMachines.List();
            azureVms.LoadAll();

            var parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = 50;

            Parallel.ForEach(azureVms, parallelOptions, (vm) =>{
                if(vm.Tags.ContainsKey("AutoShutdownSchedule"))
                {
                    Debug.WriteLine($"[Trace - {vm.Name}] Auto-Shutdown found");
                    if(vm.Inner.ProvisioningState != "Failed")
                    {
                        var powerState = vm.PowerState.ToString().Replace("PowerState/","");
                        var schedules = vm.Tags["AutoShutdownSchedule"].Split(',');
                        var shouldBeOff = schedules.Any(x => CheckScheduleEntry(x));
                        if(shouldBeOff && powerState.Contains("running"))
                        {
                            Debug.WriteLine($"[Trace - {vm.Name}] Shutting down VM");
                            if (!simulate) {
                                Task.Run(() => {azure.VirtualMachines.Deallocate(vm.ResourceGroupName,vm.Name);});
                            }
                        } else if(!shouldBeOff && powerState.Contains("deallocated")) {
                            Debug.WriteLine($"[Trace - {vm.Name}] Starting VM");
                            if(!simulate)  {
                                Task.Run(() => {azure.VirtualMachines.Start(vm.ResourceGroupName,vm.Name);});
                            }
                        }
                    } else {
                        Debug.WriteLine($"[Error - {vm.Name}] Provisioned with error");
                    }
                    
                    
                } else {
                    Debug.WriteLine($"[Trace - {vm.Name}] Auto-Shutdown not found");
                }                
            });
        }
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting to verify machine state");
            var config = Newtonsoft.Json.JsonConvert.DeserializeObject<ShutdownConfig>(File.ReadAllText("./base.config.azure"));
            foreach(var subscription in config.Subscriptions){
                AssertVms(subscription,config.Simulate);
            }
        }
        static bool CheckScheduleEntry (string timeRange)
		{	
			var currentTime = DateTime.UtcNow;
    		var midnight = DateTime.UtcNow.AddDays(1).Date;	 
            DateTime? rangeStart = null,rangeEnd = null,parsedDay = null;
            if( string.IsNullOrWhiteSpace(timeRange) || 
                timeRange.Equals("DoNotShutDown",StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
			try
			{
	    		// Parse as range if contains '->'
                if(timeRange.Contains("->"))
	    		{
                    var timeRangeComponents = Regex.Split(timeRange,"->");
	        		if(timeRangeComponents.Length == 2)
	        		{
                        rangeStart = DateTime.Parse(timeRangeComponents[0]);
                        rangeEnd = DateTime.Parse(timeRangeComponents[1]);
			
	            		// Check for crossing midnight
	            		if(rangeStart > rangeEnd)
	            		{
                    		// If current time is between the start of range and midnight tonight, interpret start time as earlier today and end time as tomorrow
                    		if(currentTime > rangeStart && currentTime < midnight)
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
                    var isDay = Enum.TryParse(timeRange,out dayOfWeek);
                    if(isDay)
                    {
                        // If specified as day of week, check if today
                        if(dayOfWeek == System.DateTime.UtcNow.DayOfWeek)
                        {
                            parsedDay = DateTime.UtcNow.Date;
                        }
                    }
	        		// Otherwise attempt to parse as a date, e.g. 'December 25'
	        		else {
                        parsedDay = DateTime.Parse(timeRange);
	        		}	    		
	        		if(parsedDay != null)
	        		{
	            		rangeStart = parsedDay; //# Defaults to midnight
	            		rangeEnd = parsedDay.Value.AddHours(23).AddMinutes(59).AddSeconds(59); //# End of the same day
	        		}
	    		}
                if(rangeStart.HasValue && rangeEnd.HasValue){
                    // Check if current time falls within range
                    return currentTime > rangeStart.Value && currentTime < rangeEnd.Value;
                } else {
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
