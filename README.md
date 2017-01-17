# Azure Auto Shutdown

This project has as a goal to have one auto shutdown script for azure.
The idea of this script it is to be a simple but parallel script that can be executed anywhere ( hence it being a .Net core project)

This working by checking the AutoShutdownTag on the virtual machines and if it is present and the time falls withing the range it stop the virtual machine is outside of the range it starts the virtual machine.
To use it put this script to run as a cron job inside any of the services ( you can even deploy it as an azurewejob on a free tier and use it for free)

# Requirements

You will need to have 2 files in the same folder to use this application 

## cred.azure
This is the authentication file to connect to the azure account. You should be suing a server principal to authenticate to this account.
### Format:
```
subscription=[subscriptionid]
client=[serverPrincipalId]
key=[serverPrincipalPassword]
tenant=[tennatId]
```
You can have more details about how to create the authentication key [here](https://github.com/Azure/azure-sdk-for-net/blob/Fluent/AUTH.md)

## base.config.azure

This is the file with the configuration used to run the application.

### Format:
```
{
    "subscriptions" : [
                "[subscription to apply the auto shutdown]"
    ],
    "simulate": [bool]
}
```
If you run with simulate=true It will simulate it but will not actually run shutdown or start any machine. ( useful to test what you are going to do)


# How to setup the schedules 

Description | Tag value
----------- | -----------
Shut down from 10PM to 6 AM UTC every day | 10pm -> 6am
Shut down from 10PM to 6 AM UTC every day (different format, same result as above) | 22:00 -> 06:00
Shut down from 8PM to 12AM and from 2AM to 7AM UTC every day (bringing online from 12-2AM for maintenance in between) | 8PM -> 12AM, 2AM -> 7AM
Shut down all day Saturday and Sunday (midnight to midnight) | Saturday, Sunday
Shut down from 2AM to 7AM UTC every day and all day on weekends | 2:00 -> 7:00, Saturday, Sunday
Shut down on Christmas Day and New Year’s Day | December 25, January 1
Shut down from 2AM to 7AM UTC every day, and all day on weekends, and on Christmas Day | 2:00 -> 7:00, Saturday, Sunday, December 25
Shut down always – I don’t want this VM online, ever | 00:00 -> 23:59:59
