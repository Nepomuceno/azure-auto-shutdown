using System;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Resource.Fluent.Authentication;
using Microsoft.Azure.Management.Resource.Fluent.Core;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace AutoShutdown
{
    public class ShutdownConfig
    {
        public bool Simulate { get; set; }
        public bool DefaultToOff { get; set; }
        public SubscriptionInfo[] Subscriptions { get; set; }
        public bool SendSlackInfo {get;set;}
        public string SlackUrl {get;set;}

    }

    public class SubscriptionInfo
    {
        public bool? Simulate { get; set; }
        public string SubscriptionId { get; set; }
        public string Name { get; set; }
        public bool? DefaultToOff { get; set; }
    }

    public class Program
    {
        public static ConcurrentDictionary<string,SubscriptionInfo> subscriptionInfo =
            new ConcurrentDictionary<string,SubscriptionInfo>();
        private static ShutdownConfig config;
        public static ConcurrentBag<string> MachinesStarted = new ConcurrentBag<string>();
        public static ConcurrentBag<string> MachinesStopped = new ConcurrentBag<string>();
        public static ConcurrentBag<string> MachinesWithTag = new ConcurrentBag<string>();
        public static ConcurrentBag<string> MachinesWithoutTag = new ConcurrentBag<string>();

        public static void AssertVms(AzureCredentials credentials, string subscriptionId)
        {
            var azure = Azure
                    .Configure()
                    .WithLogLevel(HttpLoggingDelegatingHandler.Level.NONE)
                    .Authenticate(credentials)
                    .WithSubscription(subscriptionId);

            var defaultToOff = subscriptionInfo[subscriptionId].DefaultToOff.HasValue ? subscriptionInfo[subscriptionId].DefaultToOff.Value : config.DefaultToOff;
            var simulate = subscriptionInfo[subscriptionId].DefaultToOff.HasValue ? subscriptionInfo[subscriptionId].Simulate.Value : config.Simulate;

            PagedList<Microsoft.Azure.Management.Compute.Fluent.IVirtualMachine> azureVms;

            try
            {
                 azureVms = azure.VirtualMachines.List();
                 azureVms.LoadAll();
            }
            catch (Exception ex)
            {
                azureVms = new PagedList<Microsoft.Azure.Management.Compute.Fluent.IVirtualMachine>(new List<Microsoft.Azure.Management.Compute.Fluent.IVirtualMachine>());
                Console.WriteLine(ex.Message);
            }


            var parallelOptions = new ParallelOptions();
            parallelOptions.MaxDegreeOfParallelism = 50;

            Parallel.ForEach(azureVms, parallelOptions, (vm) => {
                var shutdownKey = vm.Tags.FirstOrDefault(t => t.Key.Equals("AutoShutdownSchedule",StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(shutdownKey.Key))
                {
                    try {
                        MachinesWithTag.Add(vm.Name);
                        Debug.WriteLine($"[Trace - {vm.Name}] Auto-Shutdown found");
                        if (vm.Inner.ProvisioningState != "Failed")
                        {
                            if (!(shutdownKey.Value.ToLower() == "donotshutdown"))
                            {
                                var powerState = vm.PowerState.ToString().Replace("PowerState/","");
                                var schedules = shutdownKey.Value.Split(',');
                                var shouldBeOff = schedules.Any(x => CheckScheduleEntry(x));
                                if (shouldBeOff && powerState.Contains("running"))
                                {
                                    Debug.WriteLine($"[Trace - {vm.Name}] Shutting down VM");
                                    MachinesStopped.Add(vm.Name);
                                    if (!simulate)
                                    {
                                        Task.Run(() => {
                                            try
                                            {
                                                azure.VirtualMachines.Deallocate(vm.ResourceGroupName,vm.Name);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine(ex.Message);
                                            }
                                        });
                                    }
                                }
                                else if (!shouldBeOff && powerState.Contains("deallocated"))
                                {
                                    MachinesStarted.Add(vm.Name);
                                    Debug.WriteLine($"[Trace - {vm.Name}] Starting VM");
                                    if (!simulate)
                                    {
                                        Task.Run(() => {
                                            try
                                            {
                                                azure.VirtualMachines.Start(vm.ResourceGroupName,vm.Name);
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine(ex.Message);
                                            }
                                        });
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[Error - {vm.Name}] Provisioned with error");
                        }
                    }
                    catch
                    {
                        Console.WriteLine($"Error getting date for vm {vm.Name}");
                    }
                }
                else
                {
                    if (defaultToOff)
                    {
                        try
                        {
                            var powerState = vm.PowerState.ToString().Replace("PowerState/","");
                            if (powerState.Contains("running"))
                            {
                                MachinesStopped.Add(vm.Name);
                                if (!simulate)
                                {
                                    Task.Run(() => {
                                        try
                                        {
                                            azure.VirtualMachines.Deallocate(vm.ResourceGroupName,vm.Name);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine(ex.Message);
                                        }
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }

                    }
                    MachinesWithoutTag.Add(vm.Name);
                    Debug.WriteLine($"[Trace - {vm.Name}] Auto-Shutdown not found");
                }
            });
        }
        public static void Main(string[] args)
        {
            var credentialsPath = "./cred.azure";
            var configPath = "./base.config.azure";
            if (args.Count() == 2)
            {
                credentialsPath = args[0];
                configPath = args[1];
            }
            AzureCredentials credentials = AzureCredentials.FromFile(credentialsPath);
            Console.WriteLine("Starting to verify machine state");
            var azureList = Azure.Authenticate(credentials).Subscriptions.List();
            azureList.LoadAll();
            try
            {
                config = JsonConvert.DeserializeObject<ShutdownConfig>(File.ReadAllText(configPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            SlackPayload payload = new SlackPayload();
            foreach (var subscription in config.Subscriptions)
            {
                subscriptionInfo.GetOrAdd(subscription.SubscriptionId,subscription);
            }

            foreach (var subscription in config.Subscriptions)
            {
                AssertVms(credentials, subscription.SubscriptionId);
                if (MachinesStarted.Count > 0 || MachinesStopped.Count > 0)
                {
                    payload.Attachments.Add(ConfigureSlackMessage(azureList.Single(s => s.SubscriptionId == subscription.SubscriptionId).DisplayName, config.Simulate));
                }
                MachinesStarted = new ConcurrentBag<string>();
                MachinesStopped = new ConcurrentBag<string>();
                MachinesWithTag = new ConcurrentBag<string>();
                MachinesWithoutTag = new ConcurrentBag<string>();
            }
            if (payload.Attachments.Count > 0 && config.SendSlackInfo)
            {
                SlackClient client = new SlackClient(new Uri(config.SlackUrl));
                var response = client.SendMessageAsync(payload).Result;
                Console.WriteLine(JsonConvert.SerializeObject(response));
            }
        }


        private static SlackMessage ConfigureSlackMessage(string subscription, bool simulate)
        {
            var message = new SlackMessage();
            message.Color = simulate ? "good" : "danger";
            message.Username = "Auto Shutdown *" + subscription + "*";
            message.PreText = $"Shutdown for *{subscription}* \n  Machines with tag: {MachinesWithTag.Count}, Machines without tag: {MachinesWithoutTag.Count}";
            message.Fields = new List<SlackField>();
            message.Fields.Add(new SlackField() {
                Title = $"{MachinesStarted.Count} Machines were started:",
                Value = string.Join("\n",MachinesStarted.OrderBy(m => m)),
                Short = true
            });
            message.Fields.Add(new SlackField() {
                Title = $"{MachinesStopped.Count} Machines were stopped:",
                Value = string.Join("\n",MachinesStopped.OrderBy(m => m)),
                Short = true
            });

            return message;
        }

        static bool CheckScheduleEntry (string timeRange)
        {
            var currentTime = DateTime.UtcNow;
            var midnight = DateTime.UtcNow.AddDays(1).Date;
            DateTime? rangeStart = null,rangeEnd = null,parsedDay = null;
            if ( string.IsNullOrWhiteSpace(timeRange) ||
                timeRange.Equals("DoNotShutDown",StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            try
            {
                // Parse as range if contains '->'
                if (timeRange.Contains("->"))
                {
                    var timeRangeComponents = Regex.Split(timeRange,"->");
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
                    var isDay = Enum.TryParse(timeRange,out dayOfWeek);
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
